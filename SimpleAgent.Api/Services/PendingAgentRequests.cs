using System.Collections.Concurrent;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Api.Services;

public sealed class PendingAgentRequests
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentResultMessage>> _requests = new();

    public TaskCompletionSource<AgentResultMessage> Register(string correlationId)
    {
        var completionSource = new TaskCompletionSource<AgentResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.TryAdd(correlationId, completionSource))
        {
            throw new InvalidOperationException($"A pending request already exists for correlation ID '{correlationId}'.");
        }

        return completionSource;
    }

    public bool TryComplete(AgentResultMessage resultMessage)
    {
        if (!_requests.TryRemove(resultMessage.CorrelationId, out var completionSource))
        {
            return false;
        }

        return completionSource.TrySetResult(resultMessage);
    }

    public void Remove(string correlationId)
    {
        _requests.TryRemove(correlationId, out _);
    }
}
