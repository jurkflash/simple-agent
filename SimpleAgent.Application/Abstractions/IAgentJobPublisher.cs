using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Application.Abstractions;

public interface IAgentJobPublisher
{
    Task PublishAsync(AgentJobMessage message, CancellationToken cancellationToken = default);
}
