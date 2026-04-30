using System.Text.Json;
using SimpleAgent.Models;
using SimpleAgent.Tools;

namespace SimpleAgent;

public sealed class Agent
{
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

        while (!state.IsFinished)
        {
            var decisionJson = _llmClient.GetNextDecision(state);
            Console.WriteLine($"Decision: {decisionJson}");
            state.AddHistory($"Decision: {decisionJson}");

            var decision = Decision.FromJson(decisionJson);

            switch (decision.Action)
            {
                case DecisionAction.GetComplaints:
                    await ExecuteToolAsync(state, decision);
                    break;

                case DecisionAction.Finish:
                    state.FinalOutput = decision.Output ?? "No final output provided.";
                    state.IsFinished = true;
                    state.AddHistory($"Finished: {state.FinalOutput}");
                    Console.WriteLine("Result:");
                    Console.WriteLine(state.FinalOutput);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported action: {decision.Action}");
            }
        }
    }

    private async Task ExecuteToolAsync(AgentState state, Decision decision)
    {
        const string toolName = "get_complaints";

        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw new InvalidOperationException($"Tool '{toolName}' is not registered.");
        }

        Console.WriteLine($"Tool execution: {tool.Name}");
        state.AddHistory($"Tool execution: {tool.Name}");

        var result = await tool.ExecuteAsync(decision.Input ?? string.Empty);
        state.LastToolResult = result;
        state.AddHistory($"Tool result: {result}");

        Console.WriteLine("Result:");
        Console.WriteLine(FormatJson(result));
    }

    private static string FormatJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
