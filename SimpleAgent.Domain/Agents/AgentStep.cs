using Pokok.BuildingBlocks.Persistence.Entities;

namespace SimpleAgent.Domain.Agents;

public sealed class AgentStep : EntityBase
{
    private AgentStep()
    {
        Action = string.Empty;
    }

    internal AgentStep(
        Guid agentRunId,
        int stepNumber,
        string action,
        string? input,
        string? result,
        string? error,
        long durationMs)
    {
        AgentRunId = agentRunId;
        StepNumber = stepNumber;
        Action = action;
        Input = input;
        Result = result;
        Error = error;
        DurationMs = durationMs;
    }

    public Guid AgentRunId { get; private set; }

    public int StepNumber { get; private set; }

    public string Action { get; private set; }

    public string? Input { get; private set; }

    public string? Result { get; private set; }

    public string? Error { get; private set; }

    public long DurationMs { get; private set; }
}
