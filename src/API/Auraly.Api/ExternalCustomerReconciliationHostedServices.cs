using System.Text;
using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Azure.Messaging.ServiceBus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Auraly.Api;

public sealed record ExternalCustomerReconciliationServiceBusOptions(string QueueName);
public sealed record ExternalCustomerReconciliationRabbitMqOptions(string QueueName);

public sealed class ExternalCustomerReconciliationServiceBusHostedService(
    ServiceBusClient client,
    ExternalCustomerReconciliationServiceBusOptions options,
    IServiceScopeFactory scopes,
    ILogger<ExternalCustomerReconciliationServiceBusHostedService> logger)
    : BackgroundService
{
    private const int MaximumDeliveries = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateSessionProcessor(
            options.QueueName,
            new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentSessions = 16,
                MaxConcurrentCallsPerSession = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
                PrefetchCount = 0
            });
        processor.ProcessMessageAsync += ProcessAsync;
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(
                args.Exception,
                "External-customer reconciliation Service Bus failure for {EntityPath}.",
                args.EntityPath);
            return Task.CompletedTask;
        };
        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task ProcessAsync(ProcessSessionMessageEventArgs args)
    {
        ExternalCustomerReconciliationSignal? signal = null;
        try
        {
            signal = ExternalCustomerReconciliationSignalCodec.Deserialize(
                args.Message.Body.ToString());
            ValidateEnvelope(
                args.Message.MessageId,
                args.Message.SessionId,
                signal);
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<
                ExternalCustomerReconciliationSystemService>();
            var result = await service.ProcessAsync(signal, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            logger.LogInformation(
                "External customer {ExternalCustomerId} reconciled as {Status}; replay {Replay}.",
                result.ExternalCommerceCustomerId,
                result.Status,
                result.IdempotentReplay);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "External-customer reconciliation message {MessageId} failed on delivery {DeliveryCount}.",
                signal?.MessageId,
                args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "ExternalCustomerReconciliationDeadLettered",
                    "The message exhausted five technical processing attempts.",
                    args.CancellationToken);
                return;
            }
            await args.AbandonMessageAsync(
                args.Message,
                cancellationToken: args.CancellationToken);
        }
    }

    internal static void ValidateEnvelope(
        string messageId,
        string? businessSession,
        ExternalCustomerReconciliationSignal signal)
    {
        if (!Guid.TryParse(messageId, out var parsedMessageId) ||
            parsedMessageId != signal.MessageId)
            throw new InvalidOperationException(
                "The broker MessageId differs from the reconciliation payload.");
        if (!string.Equals(
                businessSession,
                signal.BusinessId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The broker business session differs from the reconciliation payload.");
    }
}

public sealed class ExternalCustomerReconciliationRabbitMqHostedService(
    RabbitMqProcessingConnection connections,
    ExternalCustomerReconciliationRabbitMqOptions options,
    IServiceScopeFactory scopes,
    ILogger<ExternalCustomerReconciliationRabbitMqHostedService> logger)
    : BackgroundService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connections.CreateChannelAsync(
            false,
            stoppingToken);
        await EnsureTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => ProcessAsync(channel, args);
        await channel.BasicConsumeAsync(
            options.QueueName,
            false,
            $"auraly-external-customers-{Environment.ProcessId}",
            false,
            false,
            null,
            consumer,
            stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs args)
    {
        ExternalCustomerReconciliationSignal? signal = null;
        try
        {
            signal = ExternalCustomerReconciliationSignalCodec.Deserialize(
                Encoding.UTF8.GetString(args.Body.Span));
            ExternalCustomerReconciliationServiceBusHostedService.ValidateEnvelope(
                args.BasicProperties.MessageId ?? string.Empty,
                Header(args.BasicProperties, "businessId"),
                signal);
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<
                        ExternalCustomerReconciliationSystemService>();
                    await service.ProcessAsync(signal, args.CancellationToken);
                    await channel.BasicAckAsync(
                        args.DeliveryTag,
                        false,
                        args.CancellationToken);
                    return;
                }
                catch (Exception exception) when (
                    attempt < MaximumAttempts &&
                    exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "External-customer reconciliation {MessageId} failed on attempt {Attempt}.",
                        signal.MessageId,
                        attempt);
                    await Task.Delay(RetryDelay, args.CancellationToken);
                }
            }
            throw new InvalidOperationException(
                "External-customer reconciliation exhausted its processing attempts.");
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "External-customer reconciliation message {MessageId} was dead-lettered.",
                signal?.MessageId);
            await channel.BasicNackAsync(
                args.DeliveryTag,
                false,
                false,
                args.CancellationToken);
        }
    }

    private async Task EnsureTopologyAsync(
        IChannel channel,
        CancellationToken cancellationToken)
    {
        var deadQueue = $"{options.QueueName}.dead";
        await channel.QueueDeclareAsync(
            deadQueue, true, false, false, null, false, false, cancellationToken);
        await channel.QueueDeclareAsync(
            options.QueueName,
            true,
            false,
            false,
            new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = deadQueue
            },
            false,
            false,
            cancellationToken);
    }

    private static string? Header(
        IReadOnlyBasicProperties properties,
        string name)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue(name, out var value))
            return null;
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => value?.ToString()
        };
    }
}
