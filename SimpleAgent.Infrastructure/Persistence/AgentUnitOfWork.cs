using Pokok.BuildingBlocks.Persistence.Abstractions;
using Pokok.BuildingBlocks.Persistence.Base;

namespace SimpleAgent.Infrastructure.Persistence;

public sealed class AgentUnitOfWork : UnitOfWorkBase, IUnitOfWork
{
    public AgentUnitOfWork(AgentDbContext context)
        : base(context)
    {
    }
}
