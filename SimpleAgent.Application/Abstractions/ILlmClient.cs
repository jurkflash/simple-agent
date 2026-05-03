using SimpleAgent.Domain.Models;

namespace SimpleAgent.Application.Abstractions;

public interface ILlmClient
{
    Task<string> GetNextDecisionAsync(
        AgentState state,
        int stepNumber,
        CancellationToken cancellationToken = default);
}
