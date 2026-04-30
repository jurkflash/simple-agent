using System.Text.Json;
using SimpleAgent.Models;

namespace SimpleAgent;

public sealed class LlmClient
{
    private int _callCount;

    public string GetNextDecision(AgentState state)
    {
        _callCount++;

        return _callCount switch
        {
            1 => Serialize(new Decision
            {
                Action = DecisionAction.GetComplaints,
                Input = state.Goal
            }),
            2 => Serialize(new Decision
            {
                Action = DecisionAction.Finish,
                Output = BuildSummary(state.LastToolResult)
            }),
            _ => throw new InvalidOperationException("Mock LLM only supports two calls.")
        };
    }

    private static string BuildSummary(string? toolResult)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
        {
            return "Complaint summary for April: no complaint data available.";
        }

        using var document = JsonDocument.Parse(toolResult);
        var items = document.RootElement.EnumerateArray()
            .Select(item => $"{item.GetProperty("category").GetString()}: {item.GetProperty("count").GetInt32()}")
            .ToArray();

        return "Complaint summary for April\n- " + string.Join("\n- ", items);
    }

    private static string Serialize(Decision decision)
    {
        return JsonSerializer.Serialize(decision, Decision.SerializerOptions);
    }
}
