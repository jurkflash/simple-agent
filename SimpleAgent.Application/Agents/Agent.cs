using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimpleAgent.Application.Abstractions;
using Pokok.BuildingBlocks.Persistence.Abstractions;
using SimpleAgent.Domain.Agents;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Application.Agents;

public sealed class Agent
{
    private const int MaxIterations = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly ILlmClient _llmClient;
    private readonly IAgentToolExecutor _toolExecutor;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<Agent> _logger;

    public Agent(
        ILlmClient llmClient,
        IAgentToolExecutor toolExecutor,
        ICorrelationContextAccessor correlationContextAccessor,
        IAgentRunRepository agentRunRepository,
        IUnitOfWork unitOfWork,
        ILogger<Agent> logger)
    {
        _llmClient = llmClient;
        _toolExecutor = toolExecutor;
        _correlationContextAccessor = correlationContextAccessor;
        _agentRunRepository = agentRunRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(
        string goal,
        string? correlationId = null,
        Guid? replayOfRunId = null,
        CancellationToken cancellationToken = default)
    {
        correlationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString()
            : correlationId;

        var previousCorrelationId = _correlationContextAccessor.CorrelationId;
        _correlationContextAccessor.CorrelationId = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId
        });

        var state = new AgentState(goal, correlationId);
        var agentRun = await _agentRunRepository.GetByCorrelationIdAsync(correlationId, cancellationToken)
            ?? new AgentRun(correlationId, goal, replayOfRunId);
        var totalStopwatch = Stopwatch.StartNew();
        var iterationCount = 0;
        var finalAction = "unknown";
        string? error = null;
        JsonElement lastInput = default;

        try
        {
            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Agent run started for goal {Goal}",
                correlationId,
                goal);

            if (agentRun.Id == Guid.Empty)
            {
                await _agentRunRepository.AddAsync(agentRun, cancellationToken);
                await _unitOfWork.CompleteAsync(cancellationToken);
            }
            else
            {
                EnsureReplayExecutionCanStart(agentRun, goal, replayOfRunId);
                _logger.LogInformation(
                    "[TraceId: {CorrelationId}] Reusing existing persisted run {AgentRunId} with replayOfRunId={ReplayOfRunId}",
                    correlationId,
                    agentRun.Id,
                    agentRun.ReplayOfRunId);
            }

            while (!state.IsFinished)
            {
                iterationCount++;
                var stepStopwatch = Stopwatch.StartNew();

                if (iterationCount > MaxIterations)
                {
                    error = "Max iterations reached";
                    return await CompleteRunAsync(state, agentRun, totalStopwatch, MaxIterations, false, finalAction, error, cancellationToken);
                }

                _logger.LogInformation(
                    "[TraceId: {CorrelationId}] Step {StepNumber} started",
                    correlationId,
                    iterationCount);

                var decisionResult = await GetDecisionWithRetryAsync(state, iterationCount, cancellationToken);
                if (!decisionResult.Success)
                {
                    error = decisionResult.ErrorMessage ?? "LLM call failed";
                    agentRun.AddStep(iterationCount, "llm_decision", null, null, error, 0);
                    await _unitOfWork.CompleteAsync(cancellationToken);
                    return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, false, finalAction, error, cancellationToken);
                }

                var decision = decisionResult.Decision!;
                lastInput = decision.Input;
                finalAction = AllowedActions.GetActionName(decision.Action);

                _logger.LogInformation(
                    "[TraceId: {CorrelationId}] Step {StepNumber}: LLM decision parsed for action {Action} with raw {RawDecision} and parsed input {ParsedInput}",
                    correlationId,
                    iterationCount,
                    finalAction,
                    TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatOutput(decisionResult.DecisionJson!)),
                    TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)));

                state.AddHistory($"Decision: {decisionResult.DecisionJson}");

                switch (decision.Action)
                {
                    case DecisionAction.GetComplaints:
                        var validation = ValidateActionExecution(decision);
                        _logger.LogInformation(
                            "[TraceId: {CorrelationId}] Step {StepNumber}: permission check for action {Action} returned allowed={Allowed}, valid={Valid}, error={Error}",
                            correlationId,
                            iterationCount,
                            finalAction,
                            validation.PermissionGranted,
                            validation.IsValid,
                            validation.Error);

                        if (!validation.IsValid)
                        {
                            error = validation.Error;
                            agentRun.AddStep(
                                iterationCount,
                                finalAction,
                                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)),
                                null,
                                error,
                                0);
                            await _unitOfWork.CompleteAsync(cancellationToken);
                            return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, false, finalAction, error, cancellationToken);
                        }

                        var actionResult = await ExecuteActionWithRetryAsync(state, decision, iterationCount, cancellationToken);
                        if (!actionResult.Success)
                        {
                            error = actionResult.ErrorMessage ?? "Tool execution failed after retries";
                            agentRun.AddStep(
                                iterationCount,
                                finalAction,
                                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)),
                                null,
                                error,
                                0);
                            await _unitOfWork.CompleteAsync(cancellationToken);
                            return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, false, finalAction, error, cancellationToken);
                        }

                        state.LastToolResult = actionResult.Result;
                        agentRun.AddStep(
                            iterationCount,
                            finalAction,
                            TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)),
                            TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatOutput(actionResult.Result!)),
                            null,
                            actionResult.DurationMs ?? 0);
                        await _unitOfWork.CompleteAsync(cancellationToken);
                        break;

                    case DecisionAction.Finish:
                        state.FinalOutput = decision.Output ?? string.Empty;
                        state.IsFinished = true;
                        state.AddHistory($"Finished: {state.FinalOutput}");
                        _logger.LogInformation(
                            "[TraceId: {CorrelationId}] Step {StepNumber}: finish action produced output {Output}",
                            correlationId,
                            iterationCount,
                            TraceLogSanitizer.Truncate(state.FinalOutput));
                        agentRun.AddStep(
                            iterationCount,
                            finalAction,
                            null,
                            TraceLogSanitizer.Truncate(state.FinalOutput),
                            null,
                            0);
                        await _unitOfWork.CompleteAsync(cancellationToken);
                        break;

                    default:
                        error = "Unknown action";
                        agentRun.AddStep(iterationCount, finalAction, null, null, error, 0);
                        await _unitOfWork.CompleteAsync(cancellationToken);
                        return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, false, finalAction, error, cancellationToken);
                }

                stepStopwatch.Stop();
                _logger.LogInformation(
                    "[TraceId: {CorrelationId}] Step {StepNumber} completed in {DurationMs}ms with action {Action}",
                    correlationId,
                    iterationCount,
                    stepStopwatch.ElapsedMilliseconds,
                    finalAction);
            }

            return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, true, finalAction, null, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Agent run failed with action {Action} and input {Input}",
                correlationId,
                finalAction,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(lastInput)));
            error = exception.Message;
            if (iterationCount > 0 && agentRun.Status == AgentRunStatus.Running)
            {
                agentRun.AddStep(
                    iterationCount,
                    finalAction,
                    TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(lastInput)),
                    null,
                    error,
                    0);
                await _unitOfWork.CompleteAsync(cancellationToken);
            }

            return await CompleteRunAsync(state, agentRun, totalStopwatch, iterationCount, false, finalAction, error, cancellationToken);
        }
        finally
        {
            _correlationContextAccessor.CorrelationId = previousCorrelationId;
        }
    }

    private async Task<DecisionAttemptResult> GetDecisionWithRetryAsync(
        AgentState state,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var llmStopwatch = Stopwatch.StartNew();
                var decisionJson = await _llmClient.GetNextDecisionAsync(state, stepNumber, cancellationToken);
                llmStopwatch.Stop();

                if (!Decision.TryFromJson(decisionJson, out var decision, out var error))
                {
                    lastError = error;
                    _logger.LogWarning(
                        "[TraceId: {CorrelationId}] Step {StepNumber}: decision parse failed after {DurationMs}ms on attempt {Attempt} with error {Error} and raw response {Response}",
                        state.CorrelationId,
                        stepNumber,
                        llmStopwatch.ElapsedMilliseconds,
                        attempt + 1,
                        error,
                        TraceLogSanitizer.Truncate(decisionJson));
                }
                else
                {
                    _logger.LogInformation(
                        "[TraceId: {CorrelationId}] Step {StepNumber}: LLM decision accepted in {DurationMs}ms on attempt {Attempt}",
                        state.CorrelationId,
                        stepNumber,
                        llmStopwatch.ElapsedMilliseconds,
                        attempt + 1);
                    return new DecisionAttemptResult(true, decisionJson, decision, null);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                _logger.LogError(
                    exception,
                    "[TraceId: {CorrelationId}] Step {StepNumber}: LLM decision attempt {Attempt} failed",
                    state.CorrelationId,
                    stepNumber,
                    attempt + 1);
            }

            if (attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "[TraceId: {CorrelationId}] Step {StepNumber}: retrying LLM call {Retry}/{MaxRetries} because {Error}",
                    state.CorrelationId,
                    stepNumber,
                    attempt + 1,
                    MaxRetries,
                    lastError);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        return new DecisionAttemptResult(false, null, null, lastError);
    }

    private async Task<ActionAttemptResult> ExecuteActionWithRetryAsync(
        AgentState state,
        Decision decision,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        var actionName = AllowedActions.GetActionName(decision.Action);
        state.AddHistory($"Action execution: {actionName}");
        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Step {StepNumber}: executing action {Action} with input {Input}",
            state.CorrelationId,
            stepNumber,
            actionName,
            TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)));

        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var actionStopwatch = Stopwatch.StartNew();
                var result = await _toolExecutor.ExecuteAsync(decision.Action, decision.Input, stepNumber, cancellationToken);
                actionStopwatch.Stop();

                if (ErrorResult.TryGetMessage(result, out var error))
                {
                    lastError = error;
                    _logger.LogWarning(
                        "[TraceId: {CorrelationId}] Step {StepNumber}: action {Action} returned an error in {DurationMs}ms on attempt {Attempt}: {Error}",
                        state.CorrelationId,
                        stepNumber,
                        actionName,
                        actionStopwatch.ElapsedMilliseconds,
                        attempt + 1,
                        error);
                }
                else
                {
                    state.AddHistory($"Action result: {result}");
                    _logger.LogInformation(
                        "[TraceId: {CorrelationId}] Step {StepNumber}: action {Action} succeeded in {DurationMs}ms with result {Result}",
                        state.CorrelationId,
                        stepNumber,
                        actionName,
                        actionStopwatch.ElapsedMilliseconds,
                        TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatOutput(result)));
                    return new ActionAttemptResult(true, result, null, actionStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                _logger.LogError(
                    exception,
                    "[TraceId: {CorrelationId}] Step {StepNumber}: action {Action} failed on attempt {Attempt} with input {Input}",
                    state.CorrelationId,
                    stepNumber,
                    actionName,
                    attempt + 1,
                    TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(decision.Input)));
            }

            if (attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "[TraceId: {CorrelationId}] Step {StepNumber}: retrying action {Action} {Retry}/{MaxRetries} because {Error}",
                    state.CorrelationId,
                    stepNumber,
                    actionName,
                    attempt + 1,
                    MaxRetries,
                    lastError);
            }
        }

        return new ActionAttemptResult(false, null, lastError, null);
    }

    private ActionValidationResult ValidateActionExecution(Decision decision)
    {
        return _toolExecutor.ValidateAction(decision.Action, decision.Input);
    }

    private async Task<AgentRunResult> CompleteRunAsync(
        AgentState state,
        AgentRun agentRun,
        Stopwatch totalStopwatch,
        int steps,
        bool success,
        string? finalAction,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!success)
        {
            state.FinalOutput = ErrorResult.Create(error ?? "Unexpected error");
            state.IsFinished = true;
            state.AddHistory(state.FinalOutput);
        }

        totalStopwatch.Stop();

        if (success)
        {
            agentRun.Complete(finalAction, state.FinalOutput, totalStopwatch.ElapsedMilliseconds);
        }
        else if (agentRun.Status == AgentRunStatus.Running)
        {
            agentRun.Fail(error ?? "Unexpected error", finalAction, totalStopwatch.ElapsedMilliseconds);
        }

        await _unitOfWork.CompleteAsync(cancellationToken);

        var summary = new AgentRunSummary
        {
            CorrelationId = state.CorrelationId,
            Steps = steps,
            Success = success,
            DurationMs = totalStopwatch.ElapsedMilliseconds,
            FinalAction = finalAction,
            Error = error
        };

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Agent run completed in {DurationMs}ms with success={Success}, steps={Steps}, finalAction={FinalAction}, error={Error}",
            summary.CorrelationId,
            summary.DurationMs,
            summary.Success,
            summary.Steps,
            summary.FinalAction,
            summary.Error);

        return new AgentRunResult
        {
            Output = state.FinalOutput ?? string.Empty,
            Summary = summary
        };
    }

    private static void EnsureReplayExecutionCanStart(AgentRun agentRun, string goal, Guid? replayOfRunId)
    {
        if (!string.Equals(agentRun.Goal, goal, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The persisted agent run goal does not match the queued job goal.");
        }

        if (agentRun.Status != AgentRunStatus.Running)
        {
            throw new InvalidOperationException("Only running persisted agent runs can be executed.");
        }

        if (agentRun.ReplayOfRunId != replayOfRunId)
        {
            throw new InvalidOperationException("The queued replay metadata does not match the persisted agent run.");
        }

        if (agentRun.Steps.Count > 0)
        {
            throw new InvalidOperationException("The persisted agent run has already started and cannot be resumed.");
        }
    }

    private sealed record DecisionAttemptResult(bool Success, string? DecisionJson, Decision? Decision, string? ErrorMessage);

    private sealed record ActionAttemptResult(bool Success, string? Result, string? ErrorMessage, long? DurationMs);
}
