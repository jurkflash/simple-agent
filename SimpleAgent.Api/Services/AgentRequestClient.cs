using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using RabbitMQ.Client;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Api.Services;

public sealed class AgentRequestClient
{
    private const string AgentJobsQueueName = "agent-jobs";
    private const string AgentResponsesQueueName = "agent-responses";
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    private readonly IRabbitMQConnection _rabbitMqConnection;
    private readonly PendingAgentRequests _pendingRequests;
    private readonly ILogger<AgentRequestClient> _logger;

    public AgentRequestClient(
        IRabbitMQConnection rabbitMqConnection,
        PendingAgentRequests pendingRequests,
        ILogger<AgentRequestClient> logger)
    {
        _rabbitMqConnection = rabbitMqConnection;
        _pendingRequests = pendingRequests;
        _logger = logger;
    }

    public async Task<AgentResultMessage> RunAsync(string goal, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        var completionSource = _pendingRequests.Register(correlationId);
        var message = new AgentJobMessage
        {
            Goal = goal,
            CorrelationId = correlationId,
            ReplyTo = AgentResponsesQueueName
        };

        try
        {
            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Publishing agent job with goal {Goal} to queue {QueueName}",
                correlationId,
                goal,
                AgentJobsQueueName);

            using var channel = await _rabbitMqConnection.CreateChannelAsync();
            await channel.QueueDeclareAsync(
                queue: AgentJobsQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            var payload = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(payload);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                ReplyTo = AgentResponsesQueueName
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: AgentJobsQueueName,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Waiting for agent response on queue {QueueName}",
                correlationId,
                AgentResponsesQueueName);

            return await completionSource.Task.WaitAsync(ResponseTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "[TraceId: {CorrelationId}] Timed out waiting {TimeoutSeconds}s for agent response",
                correlationId,
                ResponseTimeout.TotalSeconds);
            throw;
        }
        finally
        {
            _pendingRequests.Remove(correlationId);
        }
    }
}
