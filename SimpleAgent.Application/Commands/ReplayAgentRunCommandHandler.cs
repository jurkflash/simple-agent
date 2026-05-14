using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Abstractions;
using Pokok.BuildingBlocks.Persistence.Abstractions;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Application.Agents;
using SimpleAgent.Domain.Agents;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Application.Commands;

public sealed class ReplayAgentRunCommandHandler : ICommandHandler<ReplayAgentRunCommand, ReplayAgentRunResult>
{
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentJobPublisher _agentJobPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReplayAgentRunCommandHandler> _logger;

    public ReplayAgentRunCommandHandler(
        IAgentRunRepository agentRunRepository,
        IAgentJobPublisher agentJobPublisher,
        IUnitOfWork unitOfWork,
        ILogger<ReplayAgentRunCommandHandler> logger)
    {
        _agentRunRepository = agentRunRepository;
        _agentJobPublisher = agentJobPublisher;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ReplayAgentRunResult> HandleAsync(ReplayAgentRunCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Replay requested for agent run {AgentRunId}", command.AgentRunId);

        var originalRun = await _agentRunRepository.GetByIdAsync(command.AgentRunId, cancellationToken);
        if (originalRun is null)
        {
            _logger.LogWarning("Replay validation failed for run {AgentRunId}: run not found", command.AgentRunId);
            return ReplayAgentRunResult.Rejected(command.AgentRunId, "not_found", "Agent run was not found.");
        }

        var validation = ValidateReplay(originalRun);
        if (!validation.IsAllowed)
        {
            _logger.LogWarning(
                "[TraceId: {CorrelationId}] Replay validation failed for run {AgentRunId}: {Reason}",
                originalRun.CorrelationId,
                originalRun.Id,
                validation.Message);

            return ReplayAgentRunResult.Rejected(
                originalRun.Id,
                validation.ErrorCode,
                validation.Message,
                validation.UnsafeActions);
        }

        var replayCorrelationId = Guid.NewGuid().ToString();
        var replayRun = new AgentRun(replayCorrelationId, originalRun.Goal, originalRun.Id);

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Replay validation passed for original run {OriginalRunId}; creating replay run {ReplayRunId}",
            replayCorrelationId,
            originalRun.Id,
            replayRun.Id);

        await _agentRunRepository.AddAsync(replayRun, cancellationToken);
        await _unitOfWork.CompleteAsync(cancellationToken);

        try
        {
            await _agentJobPublisher.PublishAsync(
                new AgentJobMessage
                {
                    Goal = originalRun.Goal,
                    CorrelationId = replayCorrelationId,
                    ReplayOfRunId = originalRun.Id
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            replayRun.Fail("Replay job publishing failed.", null, 0);
            await _unitOfWork.CompleteAsync(cancellationToken);

            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Failed to publish replay job for original run {OriginalRunId} and replay run {ReplayRunId}",
                replayCorrelationId,
                originalRun.Id,
                replayRun.Id);
            throw;
        }

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Replay queued for original run {OriginalRunId} as replay run {ReplayRunId}",
            replayCorrelationId,
            originalRun.Id,
            replayRun.Id);

        return ReplayAgentRunResult.AcceptedReplay(originalRun.Id, replayRun.Id, replayCorrelationId);
    }

    private static ReplayValidationResult ValidateReplay(AgentRun originalRun)
    {
        if (originalRun.Status != AgentRunStatus.Failed)
        {
            return originalRun.Status switch
            {
                AgentRunStatus.Running => ReplayValidationResult.Reject(
                    "invalid_status",
                    "Running agent runs cannot be replayed."),
                AgentRunStatus.Completed => ReplayValidationResult.Reject(
                    "invalid_status",
                    "Successful agent runs cannot be replayed."),
                _ => ReplayValidationResult.Reject(
                    "invalid_status",
                    "Only failed agent runs can be replayed.")
            };
        }

        var unsafeActions = originalRun.Steps
            .Select(step => step.Action)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsUnsafeReplayAction)
            .OrderBy(action => action, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsafeActions.Length > 0)
        {
            return ReplayValidationResult.Reject(
                "unsafe_action",
                "Replay is not allowed because one or more recorded actions are not safely replayable.",
                unsafeActions);
        }

        return ReplayValidationResult.Allow();
    }

    private static bool IsUnsafeReplayAction(string actionName)
    {
        return !AllowedActions.TryGet(actionName, out var definition)
            || definition is null
            || !definition.Replayable
            || !definition.Idempotent;
    }

    private sealed record ReplayValidationResult(
        bool IsAllowed,
        string ErrorCode,
        string Message,
        IReadOnlyList<string>? UnsafeActions)
    {
        public static ReplayValidationResult Allow()
        {
            return new ReplayValidationResult(true, string.Empty, string.Empty, null);
        }

        public static ReplayValidationResult Reject(
            string errorCode,
            string message,
            IReadOnlyList<string>? unsafeActions = null)
        {
            return new ReplayValidationResult(false, errorCode, message, unsafeActions);
        }
    }
}
