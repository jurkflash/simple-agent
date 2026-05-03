namespace SimpleAgent.Domain.Messaging;

public sealed class AgentResultMessage
{
    public required string CorrelationId { get; init; }

    public required bool Success { get; init; }

    public string? Result { get; init; }

    public string? Error { get; init; }
}
