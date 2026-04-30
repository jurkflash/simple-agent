using Agent.Models;

namespace Agent;

public class LlmClient
{
    private int _callCount = 0;

    public Decision GetNextDecision(AgentState state)
    {
        _callCount++;

        if (_callCount == 1)
        {
            return new Decision
            {
                Action = "get_complaints",
                Input = state.Goal
            };
        }

        string toolEntry = state.History.LastOrDefault(h => h.StartsWith("Tool result:"))
            ?? string.Empty;
        string complaintsData = toolEntry.Length > 0
            ? toolEntry["Tool result:".Length..].Trim()
            : "No data available.";

        return new Decision
        {
            Action = "finish",
            Output = $"Complaint Summary for April:\n{complaintsData}"
        };
    }
}
