using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SimpleAgent.Application.Agents;
using SimpleAgent.Domain.Messaging;

namespace SimpleAgent;

public sealed class AgentWorker : BackgroundService
{
    private const string QueueName = "agent-jobs";
    private readonly IRabbitMQConnection _rabbitMqConnection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AgentWorker> _logger;
    private IChannel? _channel;

    public AgentWorker(
        IRabbitMQConnection rabbitMqConnection,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AgentWorker> logger)
    {
        _rabbitMqConnection = rabbitMqConnection;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting agent worker for queue {QueueName}", QueueName);

        _channel = await _rabbitMqConnection.CreateChannelAsync();
        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            await HandleMessageAsync(eventArgs, stoppingToken);
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Agent worker is consuming queue {QueueName}", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Agent worker cancellation requested.");
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
        AgentJobMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<AgentJobMessage>(body);
            if (message is null)
            {
                throw new InvalidOperationException("Agent job message could not be deserialized.");
            }

            if (string.IsNullOrWhiteSpace(message.Goal))
            {
                throw new InvalidOperationException("Agent job message goal is required.");
            }

            if (string.IsNullOrWhiteSpace(message.CorrelationId))
            {
                throw new InvalidOperationException("Agent job message correlation ID is required.");
            }

            if (string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                throw new InvalidOperationException("Agent job message reply queue is required.");
            }

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Message received from queue {QueueName} with goal {Goal}",
                message.CorrelationId,
                QueueName,
                message.Goal);

            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var agent = scope.ServiceProvider.GetRequiredService<Agent>();

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Starting agent job processing",
                message.CorrelationId);

            AgentResultMessage resultMessage;

            try
            {
                var result = await agent.RunAsync(message.Goal, message.CorrelationId, cancellationToken);

                _logger.LogInformation(
                    "[TraceId: {CorrelationId}] Finished agent job processing with success={Success}, finalAction={FinalAction}, output={Output}",
                    result.Summary.CorrelationId,
                    result.Summary.Success,
                    result.Summary.FinalAction,
                    TraceLogSanitizer.Truncate(result.Output));

                resultMessage = new AgentResultMessage
                {
                    CorrelationId = result.Summary.CorrelationId,
                    Success = result.Summary.Success,
                    Result = result.Summary.Success ? result.Output : null,
                    Error = result.Summary.Success ? null : result.Summary.Error ?? result.Output
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "[TraceId: {CorrelationId}] Agent execution failed for goal {Goal}",
                    message.CorrelationId,
                    message.Goal);

                resultMessage = new AgentResultMessage
                {
                    CorrelationId = message.CorrelationId,
                    Success = false,
                    Result = null,
                    Error = exception.Message
                };
            }

            await PublishResultAsync(message.ReplyTo, resultMessage, cancellationToken);
            await _channel!.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Failed processing agent job with payload {Payload}",
                message?.CorrelationId ?? "unknown",
                TraceLogSanitizer.Truncate(body));

            await _channel!.BasicNackAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken: cancellationToken);
        }
    }

    private async Task PublishResultAsync(string replyQueue, AgentResultMessage resultMessage, CancellationToken cancellationToken)
    {
        await _channel!.QueueDeclareAsync(
            queue: replyQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var payload = JsonSerializer.Serialize(resultMessage);
        var body = Encoding.UTF8.GetBytes(payload);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await _channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: replyQueue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Published agent response to queue {QueueName} with success={Success}",
            resultMessage.CorrelationId,
            replyQueue,
            resultMessage.Success);
    }
}
