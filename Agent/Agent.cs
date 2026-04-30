using System.Text.Json;
using SimpleAgent.Models;

namespace SimpleAgent;

public sealed class Agent
{
    private const int MaxIterations = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly LlmClient _llmClient;
    private readonly CqrsToolAdapter _cqrsToolAdapter;

    public Agent(LlmClient llmClient, CqrsToolAdapter cqrsToolAdapter)
    {
        _llmClient = llmClient;
        _cqrsToolAdapter = cqrsToolAdapter;
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
                        if (!TryValidateActionExecution(decision, out var validationError))
                        {
                            StopWithError(state, iterationCount, validationError);
                            return;
                        }

                        var actionResult = await ExecuteActionWithRetryAsync(state, decision);
                        if (!actionResult.Success)
                        {
                            StopWithError(state, iterationCount, "Tool execution failed after retries", actionResult.ErrorMessage);
                            return;
                        }

                        state.LastToolResult = actionResult.Result;
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

    private async Task<ActionAttemptResult> ExecuteActionWithRetryAsync(AgentState state, Decision decision)
    {
        var actionName = GetActionName(decision.Action);

        Console.WriteLine("Action input:");
        Console.WriteLine(FormatJson(decision.Input));
        state.AddHistory($"Action execution: {actionName}");

        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await _cqrsToolAdapter.ExecuteAsync(decision.Action, decision.Input);
                if (ErrorResult.TryGetMessage(result, out var error))
                {
                    lastError = error;
                }
                else
                {
                    state.AddHistory($"Action result: {result}");
                    Console.WriteLine("Action output:");
                    Console.WriteLine(FormatOutput(result));
                    return new ActionAttemptResult(true, result, null);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }

            if (attempt < MaxRetries)
            {
                Console.WriteLine($"[Retry {attempt + 1}/{MaxRetries}] Action {actionName} failed: {lastError}");
            }
        }

        return new ActionAttemptResult(false, null, lastError);
    }

    private bool TryValidateActionExecution(Decision decision, out string error)
    {
        return _cqrsToolAdapter.TryValidateAction(decision.Action, decision.Input, out error);
    }

    private static string GetActionName(DecisionAction action)
    {
        return action switch
        {
            DecisionAction.GetComplaints => "get_complaints",
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

    private sealed record ActionAttemptResult(bool Success, string? Result, string? ErrorMessage);
}
