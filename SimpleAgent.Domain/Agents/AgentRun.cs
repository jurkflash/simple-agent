using Pokok.BuildingBlocks.Persistence.Entities;

namespace SimpleAgent.Domain.Agents;

public sealed class AgentRun : EntityBase
{
    private readonly List<AgentStep> _steps = [];

    private AgentRun()
    {
        CorrelationId = string.Empty;
        Goal = string.Empty;
    }

    public AgentRun(string correlationId, string goal, Guid? replayOfRunId = null)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(goal))
        {
            throw new ArgumentException("Goal is required.", nameof(goal));
        }

        CorrelationId = correlationId;
        Goal = goal;
        ReplayOfRunId = replayOfRunId;
        Status = AgentRunStatus.Running;
    }

    public string CorrelationId { get; private set; }

    public string Goal { get; private set; }

    public Guid? ReplayOfRunId { get; private set; }

    public bool IsReplay => ReplayOfRunId.HasValue;

    public AgentRunStatus Status { get; private set; }

    public string? FinalAction { get; private set; }

    public string? Result { get; private set; }

    public string? Error { get; private set; }

    public long? DurationMs { get; private set; }

    public IReadOnlyCollection<AgentStep> Steps => _steps.AsReadOnly();

    public void AddStep(int stepNumber, string action, string? input, string? result, string? error, long durationMs)
    {
        EnsureRunning();

        if (stepNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepNumber), "Step number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }

        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be non-negative.");
        }

        _steps.Add(new AgentStep(Id, stepNumber, action, input, result, error, durationMs));
    }

    public void Complete(string? finalAction, string? result, long durationMs)
    {
        EnsureRunning();

        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be non-negative.");
        }

        Status = AgentRunStatus.Completed;
        FinalAction = finalAction;
        Result = result;
        Error = null;
        DurationMs = durationMs;
    }

    public void Fail(string error, string? finalAction, long durationMs)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Error is required.", nameof(error));
        }

        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be non-negative.");
        }

        if (Status is AgentRunStatus.Completed or AgentRunStatus.Failed)
        {
            throw new InvalidOperationException("The agent run has already finished.");
        }

        Status = AgentRunStatus.Failed;
        FinalAction = finalAction;
        Result = null;
        Error = error;
        DurationMs = durationMs;
    }

    private void EnsureRunning()
    {
        if (Status != AgentRunStatus.Running)
        {
            throw new InvalidOperationException("Steps can only be added while the agent run is still running.");
        }
    }
}

public enum AgentRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2
}
