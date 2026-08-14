using System.Text;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Auraly.Api;

public sealed class RabbitMqAccountingProcessingHostedService(
    RabbitMqProcessingConnection connections,
    RabbitMqProcessingTransport transport,
    RabbitMqProcessingOptions options,
    IServiceScopeFactory scopes,
    ILogger<RabbitMqAccountingProcessingHostedService> logger) : BackgroundService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await transport.EnsureTopologyAsync(stoppingToken);
        await using var channel =
            await connections.CreateChannelAsync(false, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
            await ProcessAsync(channel, args);
        await channel.BasicConsumeAsync(
            options.AccountingQueueName,
            autoAck: false,
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

    private async Task ProcessAsync(
        IChannel channel,
        BasicDeliverEventArgs args)
    {
        AccountingProcessingSignal? signal = null;
        try
        {
            signal = AccountingProcessingSignalCodec.Deserialize(
                Encoding.UTF8.GetString(args.Body.Span));
            AccountingProcessingSignalCodec.Validate(signal);
            ValidateBusinessHeader(args.BasicProperties, signal.BusinessId);

            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider
                        .GetRequiredService<SqlAccountingPostingProcessor>();
                    await processor.ProcessAsync(
                        signal.DocumentId,
                        signal.DocumentType,
                        signal.BusinessId,
                        args.CancellationToken);
                    await channel.BasicAckAsync(
                        args.DeliveryTag, false, args.CancellationToken);
                    return;
                }
                catch (Exception exception) when (
                    attempt < MaximumAttempts &&
                    exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "Accounting attempt {Attempt} failed for {DocumentType} {DocumentId}.",
                        attempt,
                        signal.DocumentType,
                        signal.DocumentId);
                    await Task.Delay(RetryDelay, args.CancellationToken);
                }
            }

            throw new InvalidOperationException(
                "Accounting processing exhausted its retries.");
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Accounting was dead-lettered for {DocumentType} {DocumentId}.",
                signal?.DocumentType,
                signal?.DocumentId);
            await channel.BasicNackAsync(
                args.DeliveryTag,
                multiple: false,
                requeue: false,
                args.CancellationToken);
        }
    }

    private static void ValidateBusinessHeader(
        IReadOnlyBasicProperties properties,
        Guid businessId)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue("businessId", out var raw) ||
            !string.Equals(
                HeaderText(raw),
                businessId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "RabbitMQ businessId header differs from the accounting signal.");
    }

    private static string? HeaderText(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
        _ => value?.ToString()
    };
}
