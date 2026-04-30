namespace Agent.Models;

public class AgentState
{
    public string Goal { get; set; } = string.Empty;
    public List<string> History { get; set; } = new();
    public bool IsFinished { get; set; } = false;
}
