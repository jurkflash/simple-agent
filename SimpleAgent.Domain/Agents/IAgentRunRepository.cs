namespace SimpleAgent.Domain.Agents;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun agentRun, CancellationToken cancellationToken = default);

    Task<AgentRun?> GetByIdAsync(Guid agentRunId, CancellationToken cancellationToken = default);

    Task<AgentRun?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
}
