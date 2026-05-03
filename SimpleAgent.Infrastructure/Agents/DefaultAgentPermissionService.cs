using SimpleAgent.Application.Abstractions;
using SimpleAgent.Application.Agents;

namespace SimpleAgent.Infrastructure.Agents;

public sealed class DefaultAgentPermissionService : IAgentPermissionService
{
    public bool IsActionAllowed(string actionName)
    {
        return AllowedActions.TryGet(actionName, out _);
    }
}
