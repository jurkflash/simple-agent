namespace SimpleAgent.Application.Abstractions;

public interface IAgentPermissionService
{
    bool IsActionAllowed(string actionName);
}
