using System.Text.Json;
using SimpleAgent.Models;
using SimpleAgent.Tools;

namespace SimpleAgent;

public sealed class Agent
{
    private const int MaxIterations = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly LlmClient _llmClient;
    private readonly Dictionary<string, ITool> _tools;

    public Agent(LlmClient llmClient, IEnumerable<ITool> tools)
    {
        _llmClient = llmClient;
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task RunAsync(string goal)
    {
        var state = new AgentState(goal);
        try
        {
            var iterationCount = 0;

            while (!state.IsFinished)
            {
                iterationCount++;

                if (iterationCount > MaxIterations)
                {
                    StopWithError(state, iterationCount, "Max iterations reached");
                    return;
                }

                var decisionResult = await GetDecisionWithRetryAsync(state);
                if (!decisionResult.Success)
                {
                    StopWithError(state, iterationCount, "LLM call failed", decisionResult.ErrorMessage);
                    return;
                }

                var decision = decisionResult.Decision!;
                Console.WriteLine($"Step {iterationCount}");
                Console.WriteLine($"Action: {GetActionName(decision.Action)}");
                Console.WriteLine("Decision:");
                Console.WriteLine(FormatOutput(decisionResult.DecisionJson!));
                state.AddHistory($"Decision: {decisionResult.DecisionJson}");

                switch (decision.Action)
                {
                    case DecisionAction.GetComplaints:
                    case DecisionAction.FormatReport:
                        if (!TryValidateToolExecution(decision, out var tool, out var validationError))
                        {
                            StopWithError(state, iterationCount, validationError);
                            return;
                        }

                        var toolResult = await ExecuteToolWithRetryAsync(state, tool!, decision);
                        if (!toolResult.Success)
                        {
                            StopWithError(state, iterationCount, "Tool execution failed after retries", toolResult.ErrorMessage);
                            return;
                        }

                        state.LastToolResult = toolResult.Result;
                        break;

                    case DecisionAction.Finish:
                        state.FinalOutput = decision.Output ?? string.Empty;
                        state.IsFinished = true;
                        state.AddHistory($"Finished: {state.FinalOutput}");
                        Console.WriteLine("Final output:");
                        Console.WriteLine(state.FinalOutput);
                        break;

                    default:
                        StopWithError(state, iterationCount, "Unknown action");
                        return;
                }

                Console.WriteLine();
            }
        }
        catch (Exception exception)
        {
            StopWithError(state, 0, "Unexpected error", exception.Message);
        }
    }

    private async Task<DecisionAttemptResult> GetDecisionWithRetryAsync(AgentState state)
    {
        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var decisionJson = await _llmClient.GetNextDecisionAsync(state);
                if (!Decision.TryFromJson(decisionJson, out var decision, out var error))
                {
                    lastError = error;
                }
                else
                {
                    return new DecisionAttemptResult(true, decisionJson, decision, null);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }

            if (attempt < MaxRetries)
            {
                Console.WriteLine($"[Retry {attempt + 1}/{MaxRetries}] LLM call failed: {lastError}");
                await Task.Delay(RetryDelay);
            }
        }

        return new DecisionAttemptResult(false, null, null, lastError);
    }

    private async Task<ToolAttemptResult> ExecuteToolWithRetryAsync(AgentState state, ITool tool, Decision decision)
    {
        Console.WriteLine($"Tool: {tool.Name}");
        Console.WriteLine("Tool input:");
        Console.WriteLine(FormatJson(decision.Input));
        state.AddHistory($"Tool execution: {tool.Name}");

        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await tool.ExecuteAsync(decision.Input);
                if (ErrorResult.TryGetMessage(result, out var error))
                {
                    lastError = error;
                }
                else
                {
                    state.AddHistory($"Tool result: {result}");
                    Console.WriteLine("Tool output:");
                    Console.WriteLine(FormatOutput(result));
                    return new ToolAttemptResult(true, result, null);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }

            if (attempt < MaxRetries)
            {
                Console.WriteLine($"[Retry {attempt + 1}/{MaxRetries}] Tool {tool.Name} failed: {lastError}");
            }
        }

        return new ToolAttemptResult(false, null, lastError);
    }

    private bool TryValidateToolExecution(Decision decision, out ITool? tool, out string error)
    {
        if (!TryGetToolName(decision.Action, out var toolName))
        {
            tool = null;
            error = "Unknown action";
            return false;
        }

        if (!_tools.TryGetValue(toolName, out tool))
        {
            error = $"Tool '{toolName}' not found.";
            return false;
        }

        if (!tool.TryValidateInput(decision.Input, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetToolName(DecisionAction action, out string toolName)
    {
        switch (action)
        {
            case DecisionAction.GetComplaints:
                toolName = "get_complaints";
                return true;
            case DecisionAction.FormatReport:
                toolName = "format_report";
                return true;
            default:
                toolName = string.Empty;
                return false;
        }
    }

    private static string GetActionName(DecisionAction action)
    {
        return action switch
        {
            DecisionAction.GetComplaints => "get_complaints",
            DecisionAction.FormatReport => "format_report",
            DecisionAction.Finish => "finish",
            _ => "unknown"
        };
    }

    private static void StopWithError(AgentState state, int iterationCount, string error, string? detail = null)
    {
        state.FinalOutput = ErrorResult.Create(error);
        state.IsFinished = true;
        state.AddHistory(state.FinalOutput);
        Console.WriteLine($"Step {iterationCount}");
        Console.WriteLine("Action: Error");
        Console.WriteLine("Failure reason:");
        Console.WriteLine(detail ?? error);
        Console.WriteLine("Result:");
        Console.WriteLine(state.FinalOutput);
    }

    private static string FormatJson(JsonElement input)
    {
        if (input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "null";
        }

        return JsonSerializer.Serialize(input, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string FormatOutput(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return output;
        }
    }

    private sealed record DecisionAttemptResult(bool Success, string? DecisionJson, Decision? Decision, string? ErrorMessage);

    private sealed record ToolAttemptResult(bool Success, string? Result, string? ErrorMessage);
}
