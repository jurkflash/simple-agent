using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent.Api.Services;

public sealed class AgentResponseListener : BackgroundService
{
    private const string AgentResponsesQueueName = "agent-responses";

    private readonly IRabbitMQConnection _rabbitMqConnection;
    private readonly PendingAgentRequests _pendingRequests;
    private readonly ILogger<AgentResponseListener> _logger;
    private IChannel? _channel;

    public AgentResponseListener(
        IRabbitMQConnection rabbitMqConnection,
        PendingAgentRequests pendingRequests,
        ILogger<AgentResponseListener> logger)
    {
        _rabbitMqConnection = rabbitMqConnection;
        _pendingRequests = pendingRequests;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting agent response listener for queue {QueueName}", AgentResponsesQueueName);

        _channel = await _rabbitMqConnection.CreateChannelAsync();
        await _channel.QueueDeclareAsync(
            queue: AgentResponsesQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            await HandleMessageAsync(eventArgs, stoppingToken);
        };

        await _channel.BasicConsumeAsync(
            queue: AgentResponsesQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Agent response listener cancellation requested.");
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetString(eventArgs.Body.ToArray());

        try
        {
            var resultMessage = JsonSerializer.Deserialize<AgentResultMessage>(payload);
            if (resultMessage is null)
            {
                throw new InvalidOperationException("Agent result message could not be deserialized.");
            }

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Agent response received with success={Success}",
                resultMessage.CorrelationId,
                resultMessage.Success);

            if (!_pendingRequests.TryComplete(resultMessage))
            {
                _logger.LogWarning(
                    "[TraceId: {CorrelationId}] No pending API request matched the agent response",
                    resultMessage.CorrelationId);
            }

            await _channel!.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed processing agent response payload {Payload}", payload);
            await _channel!.BasicNackAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken: cancellationToken);
        }
    }
}
