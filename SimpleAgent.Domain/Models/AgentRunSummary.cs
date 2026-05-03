namespace SimpleAgent.Domain.Models;

public sealed class AgentRunSummary
{
    public required string CorrelationId { get; init; }

    public required int Steps { get; init; }

    public required bool Success { get; init; }

    public required long DurationMs { get; init; }

    public string? FinalAction { get; init; }

    public string? Error { get; init; }
}

public sealed class AgentRunResult
{
    public required string Output { get; init; }

    public required AgentRunSummary Summary { get; init; }
}
