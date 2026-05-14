using Microsoft.EntityFrameworkCore;
using Pokok.BuildingBlocks.Persistence.Base;
using SimpleAgent.Domain.Agents;

namespace SimpleAgent.Infrastructure.Persistence;

public sealed class AgentRunRepository : RepositoryBase<AgentRun>, IAgentRunRepository
{
    public AgentRunRepository(AgentDbContext context)
        : base(context)
    {
    }

    public async Task<AgentRun?> GetByIdAsync(Guid agentRunId, CancellationToken cancellationToken = default)
    {
        return await ((AgentDbContext)Context).AgentRuns
            .Include(run => run.Steps)
            .SingleOrDefaultAsync(run => run.Id == agentRunId, cancellationToken);
    }

    public async Task<AgentRun?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        return await ((AgentDbContext)Context).AgentRuns
            .Include(run => run.Steps)
            .SingleOrDefaultAsync(run => run.CorrelationId == correlationId, cancellationToken);
    }
}
