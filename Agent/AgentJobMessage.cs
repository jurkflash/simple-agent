namespace SimpleAgent;

public sealed class AgentJobMessage
{
    public required string Goal { get; init; }

    public required string CorrelationId { get; init; }
}
