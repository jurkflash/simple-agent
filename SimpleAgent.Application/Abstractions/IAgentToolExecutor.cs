using System.Text.Json;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Application.Abstractions;

public interface IAgentToolExecutor
{
    ActionValidationResult ValidateAction(DecisionAction action, JsonElement input);

    Task<string> ExecuteAsync(
        DecisionAction action,
        JsonElement input,
        int stepNumber,
        CancellationToken cancellationToken = default);
}

public sealed record ActionValidationResult(bool IsValid, bool PermissionGranted, string Error);
