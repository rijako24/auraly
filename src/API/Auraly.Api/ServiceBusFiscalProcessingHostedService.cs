using System.Text.Json;
using Auraly.Application.Fiscal;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record FiscalProcessingServiceBusOptions(string QueueName);

public sealed class FiscalProcessingHostedService(
    ServiceBusClient client,
    FiscalProcessingServiceBusOptions options,
    IServiceScopeFactory scopes,
    FiscalStatusSynchronizationNotifier synchronization,
    ILogger<FiscalProcessingHostedService> logger) : BackgroundService
{
    private const int MaximumDeliveries = 5;
    private readonly string workerId = $"{Environment.MachineName}:fiscal:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateSessionProcessor(
            options.QueueName,
            new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentSessions = 16,
                MaxConcurrentCallsPerSession = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                PrefetchCount = 0
            });
        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;
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

    private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
    {
        FiscalProcessingSignal? signal = null;
        try
        {
            signal = FiscalProcessingSignalCodec.Deserialize(args.Message.Body.ToString());
            FiscalProcessingSignalCodec.Validate(signal);
            if (!string.Equals(
                    args.Message.SessionId,
                    signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service Bus SessionId differs from the fiscal BusinessId.");

            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<FiscalProcessingMessageHandler>();
            await handler.ProcessAsync(signal, workerId, args.CancellationToken);

            await synchronization.DispatchAsync(signal.BusinessId, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Fiscal {Stage} failed for document {DocumentId} on delivery {DeliveryCount}.",
                signal?.Stage,
                signal?.DocumentId,
                args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "FiscalProcessingNeedsIntervention",
                    "The fiscal document could not be processed after five deliveries.",
                    args.CancellationToken);
                return;
            }

            await args.AbandonMessageAsync(
                args.Message,
                cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Fiscal Service Bus processing failed for {EntityPath} from {ErrorSource}.",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }
}
