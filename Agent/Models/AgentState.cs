namespace SimpleAgent.Models;

public sealed class AgentState
{
    public AgentState(string goal)
    {
        Goal = goal;
    }

    public string Goal { get; }

    public bool IsFinished { get; set; }

    public string? LastToolResult { get; set; }

    public string? FinalOutput { get; set; }

    public List<string> History { get; } = new();

    public void AddHistory(string entry)
    {
        History.Add(entry);
    }
}
