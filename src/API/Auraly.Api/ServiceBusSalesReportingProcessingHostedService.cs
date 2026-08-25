using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Infrastructure.Persistence;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record SalesReportingProcessingServiceBusOptions(string QueueName);

public sealed class ServiceBusSalesReportingProcessingPublisher(
    ServiceBusClient client,
    SalesReportingProcessingServiceBusOptions options)
    : ISalesReportingProcessingSignalPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender sender = client.CreateSender(options.QueueName);

    public async Task PublishAsync(
        SalesReportingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        SalesReportingProcessingSignalCodec.Validate(signal);
        var message = new ServiceBusMessage(BinaryData.FromString(
            SalesReportingProcessingSignalCodec.Serialize(signal)))
        {
            MessageId = signal.SignalId.ToString("D"),
            SessionId = signal.BusinessId.ToString("D"),
            Subject = signal.DocumentType,
            ContentType = "application/json"
        };
        message.ApplicationProperties["documentId"] = signal.DocumentId.ToString("D");
        await sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => sender.DisposeAsync();
}

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

internal static class SalesReportingProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(SalesReportingProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static SalesReportingProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<SalesReportingProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException("The sales-reporting signal is invalid.");

    public static void Validate(SalesReportingProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.SignalId == Guid.Empty || signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty || signal.SourceVersion<=0 ||
            !SalesReportingProcessingPolicy.Supports(signal.DocumentType))
            throw new InvalidOperationException(
                "The sales-reporting signal has invalid identifiers or document type.");
    }
}
