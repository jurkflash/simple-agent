namespace SimpleAgent.Application.Commands;

public sealed class ReplayAgentRunResult
{
    public required Guid OriginalRunId { get; init; }

    public Guid? ReplayRunId { get; init; }

    public string? CorrelationId { get; init; }

    public bool Accepted { get; init; }

    public ReplayAgentRunError? Error { get; init; }

    public static ReplayAgentRunResult AcceptedReplay(Guid originalRunId, Guid replayRunId, string correlationId)
    {
        return new ReplayAgentRunResult
        {
            OriginalRunId = originalRunId,
            ReplayRunId = replayRunId,
            CorrelationId = correlationId,
            Accepted = true
        };
    }

    public static ReplayAgentRunResult Rejected(
        Guid originalRunId,
        string errorCode,
        string errorMessage,
        IReadOnlyList<string>? unsafeActions = null)
    {
        return new ReplayAgentRunResult
        {
            OriginalRunId = originalRunId,
            Accepted = false,
            Error = new ReplayAgentRunError
            {
                Code = errorCode,
                Message = errorMessage,
                UnsafeActions = unsafeActions
            }
        };
    }
}

public sealed class ReplayAgentRunError
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<string>? UnsafeActions { get; init; }
}
