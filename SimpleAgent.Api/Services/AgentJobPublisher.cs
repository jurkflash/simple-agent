using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using RabbitMQ.Client;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Api.Services;

public sealed class AgentJobPublisher : IAgentJobPublisher
{
    private const string AgentJobsQueueName = "agent-jobs";

    private readonly IRabbitMQConnection _rabbitMqConnection;
    private readonly ILogger<AgentJobPublisher> _logger;

    public AgentJobPublisher(IRabbitMQConnection rabbitMqConnection, ILogger<AgentJobPublisher> logger)
    {
        _rabbitMqConnection = rabbitMqConnection;
        _logger = logger;
    }

    public async Task PublishAsync(AgentJobMessage message, CancellationToken cancellationToken = default)
    {
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
            ReplyTo = message.ReplyTo
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: AgentJobsQueueName,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Published agent job to queue {QueueName} with replayOfRunId={ReplayOfRunId}",
            message.CorrelationId,
            AgentJobsQueueName,
            message.ReplayOfRunId);
    }
}
