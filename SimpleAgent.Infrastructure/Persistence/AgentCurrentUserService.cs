using Pokok.BuildingBlocks.Common;
using SimpleAgent.Application.Abstractions;

namespace SimpleAgent.Infrastructure.Persistence;

public sealed class AgentCurrentUserService : ICurrentUserService
{
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public AgentCurrentUserService(ICorrelationContextAccessor correlationContextAccessor)
    {
        _correlationContextAccessor = correlationContextAccessor;
    }

    public string? UserId => _correlationContextAccessor.CorrelationId;
}
