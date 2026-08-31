using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record AccountingProcessingServiceBusOptions(string QueueName);

public sealed class AccountingProcessingHostedService(
    ServiceBusClient client,
    AccountingProcessingServiceBusOptions options,
    IServiceScopeFactory scopes,
    ILogger<AccountingProcessingHostedService> logger) : BackgroundService
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
        AccountingProcessingSignal? signal = null;
        try
        {
            signal = AccountingProcessingSignalCodec.Deserialize(
                args.Message.Body.ToString());
            AccountingProcessingSignalCodec.Validate(signal);
            if (!string.Equals(
                    args.Message.SessionId,
                    signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service Bus SessionId differs from the accounting BusinessId.");

            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider
                .GetRequiredService<SqlAccountingPostingProcessor>();
            await processor.ProcessAsync(
                signal.DocumentId,
                signal.DocumentType,
                signal.BusinessId,
                args.CancellationToken);
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
                "Accounting failed for {DocumentType} {DocumentId} on delivery {DeliveryCount}.",
                signal?.DocumentType,
                signal?.DocumentId,
                args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "AccountingProcessingNeedsIntervention",
                    "The accounting posting could not be processed after five deliveries.",
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
            "Accounting Service Bus processing failed for {EntityPath} from {ErrorSource}.",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }
}
