using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record SalesReportingProcessingServiceBusOptions(string QueueName);

public sealed class SalesReportingProcessingHostedService(
    ServiceBusClient client,
    SalesReportingProcessingServiceBusOptions options,
    IServiceScopeFactory scopes,
    ILogger<SalesReportingProcessingHostedService> logger) : BackgroundService
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
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception,
                "Sales reporting Service Bus processing failed for {EntityPath}.",
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

    private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
    {
        SalesReportingProcessingSignal? signal = null;
        try
        {
            signal = SalesReportingProcessingSignalCodec.Deserialize(args.Message.Body.ToString());
            SalesReportingProcessingSignalCodec.Validate(signal);
            if (!string.Equals(args.Message.SessionId, signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service Bus SessionId differs from the reporting BusinessId.");

            await using var scope = scopes.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
            await worker.ProcessAsync(
                signal.DocumentId, signal.DocumentType, signal.BusinessId,
                signal.SourceVersion,
                args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Sales reporting failed for {DocumentType} {DocumentId} on delivery {DeliveryCount}.",
                signal?.DocumentType, signal?.DocumentId, args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    "SalesReportingNeedsIntervention",
                    "The sales projection failed after five deliveries.",
                    args.CancellationToken);
                return;
            }
            await args.AbandonMessageAsync(
                args.Message, cancellationToken: args.CancellationToken);
        }
    }
}
