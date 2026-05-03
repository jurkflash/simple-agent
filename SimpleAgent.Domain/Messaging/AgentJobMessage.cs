namespace SimpleAgent.Domain.Messaging;

public sealed class AgentJobMessage
{
    public required string Goal { get; init; }

    public required string CorrelationId { get; init; }

    public required string ReplyTo { get; init; }
}
