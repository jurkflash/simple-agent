using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Application.Agents;
using SimpleAgent.Application.Queries;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Infrastructure.Agents;

public sealed class CqrsToolAdapter : IAgentToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IAgentPermissionService _permissionService;
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ILogger<CqrsToolAdapter> _logger;

    public CqrsToolAdapter(
        IQueryDispatcher queryDispatcher,
        IAgentPermissionService permissionService,
        ICorrelationContextAccessor correlationContextAccessor,
        ILogger<CqrsToolAdapter> logger)
    {
        _queryDispatcher = queryDispatcher;
        _permissionService = permissionService;
        _correlationContextAccessor = correlationContextAccessor;
        _logger = logger;
    }

    public ActionValidationResult ValidateAction(DecisionAction action, JsonElement input)
    {
        var actionName = AllowedActions.GetActionName(action);
        var permitted = IsPermitted(actionName);

        if (!permitted)
        {
            return new ActionValidationResult(false, false, "Action not permitted");
        }

        return action switch
        {
            DecisionAction.GetComplaints => CreateValidationResult(
                permitted,
                TryParseGetComplaintsQuery(input, out _, out var error),
                error),
            _ => CreateValidationResult(permitted, false, "Action not permitted")
        };
    }

    public async Task<string> ExecuteAsync(
        DecisionAction action,
        JsonElement input,
        int stepNumber,
        CancellationToken cancellationToken = default)
    {
        var actionName = AllowedActions.GetActionName(action);
        var correlationId = _correlationContextAccessor.CorrelationId ?? "unknown";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!IsPermitted(actionName))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "[TraceId: {CorrelationId}] Step {StepNumber}: action {Action} was denied before execution",
                    correlationId,
                    stepNumber,
                    actionName);
                return ErrorResult.Create("Action not permitted");
            }

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Step {StepNumber}: CQRS dispatch starting for action {Action} with input {Input}",
                correlationId,
                stepNumber,
                actionName,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(input)));

            var result = action switch
            {
                DecisionAction.GetComplaints => await ExecuteGetComplaintsAsync(input, stepNumber, cancellationToken),
                _ => ErrorResult.Create("Action not permitted")
            };

            stopwatch.Stop();
            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Step {StepNumber}: CQRS dispatch completed for action {Action} in {DurationMs}ms with result {Result}",
                correlationId,
                stepNumber,
                actionName,
                stopwatch.ElapsedMilliseconds,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatOutput(result)));

            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Step {StepNumber}: CQRS execution failed for action {Action} after {DurationMs}ms with input {Input}",
                correlationId,
                stepNumber,
                actionName,
                stopwatch.ElapsedMilliseconds,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(input)));
            return ErrorResult.Create(exception.Message);
        }
    }

    private async Task<string> ExecuteGetComplaintsAsync(JsonElement input, int stepNumber, CancellationToken cancellationToken)
    {
        var actionName = AllowedActions.GetComplaints.Name;
        var correlationId = _correlationContextAccessor.CorrelationId ?? "unknown";

        if (!TryParseGetComplaintsQuery(input, out var query, out var error))
        {
            _logger.LogWarning(
                "[TraceId: {CorrelationId}] Step {StepNumber}: mapped query validation failed for action {Action} with error {Error} and input {Input}",
                correlationId,
                stepNumber,
                actionName,
                error,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(input)));
            return ErrorResult.Create(error);
        }

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Step {StepNumber}: mapped query for action {Action} is {Query}",
            correlationId,
            stepNumber,
            actionName,
            JsonSerializer.Serialize(query, JsonOptions));

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _queryDispatcher.DispatchAsync<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>>(query!, cancellationToken);
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);
            stopwatch.Stop();

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Step {StepNumber}: CQRS handler completed for action {Action} in {DurationMs}ms with result {Result}",
                correlationId,
                stepNumber,
                actionName,
                stopwatch.ElapsedMilliseconds,
                TraceLogSanitizer.Truncate(resultJson));

            return resultJson;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Step {StepNumber}: CQRS handler failed for action {Action} with input {Input}",
                correlationId,
                stepNumber,
                actionName,
                TraceLogSanitizer.Truncate(TraceLogSanitizer.FormatJson(input)));
            return ErrorResult.Create(exception.Message);
        }
    }

    private static bool TryParseGetComplaintsQuery(JsonElement input, out GetComplaintsQuery? query, out string error)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            query = null;
            error = "Input must be a JSON object with a 'month' property.";
            return false;
        }

        try
        {
            query = input.Deserialize<GetComplaintsQuery>(JsonOptions);
        }
        catch (JsonException)
        {
            query = null;
            error = "Input could not be deserialized.";
            return false;
        }

        if (query is null)
        {
            error = "Input could not be deserialized.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.Month))
        {
            error = "Month is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool IsPermitted(string actionName)
    {
        var allowed = _permissionService.IsActionAllowed(actionName);
        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Permission check for action {Action}: {Allowed}",
            _correlationContextAccessor.CorrelationId ?? "unknown",
            actionName,
            allowed);
        return allowed;
    }

    private static ActionValidationResult CreateValidationResult(bool permissionGranted, bool isValid, string error)
    {
        return new ActionValidationResult(isValid, permissionGranted, error);
    }

}
