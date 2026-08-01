using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record DocumentProcessingServiceBusOptions(string QueueName);

public sealed class ServiceBusDocumentProcessingPublisher(ServiceBusSender sender)
    : IDocumentProcessingSignalPublisher
{
    public async Task PublishAsync(DocumentProcessingSignal signal, CancellationToken cancellationToken = default)
    {
        DocumentProcessingSignalCodec.Validate(signal);
        var message = new ServiceBusMessage(BinaryData.FromString(
            DocumentProcessingSignalCodec.Serialize(signal)))
        {
            MessageId = signal.MovementId.ToString("D"),
            SessionId = signal.BusinessId.ToString("D"),
            Subject = signal.DocumentType,
            ContentType = "application/json"
        };
        message.ApplicationProperties["documentId"] = signal.DocumentId.ToString("D");
        await sender.SendMessageAsync(message, cancellationToken);
    }
}

public sealed class DocumentProcessingHostedService(
    ServiceBusClient client,
    DocumentProcessingServiceBusOptions options,
    IServiceScopeFactory scopeFactory,
    FiscalProcessingCoordinator fiscalProcessing,
    ILogger<DocumentProcessingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
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
        DocumentProcessingSignal? signal = null;
        try
        {
            signal = DocumentProcessingSignalCodec.Deserialize(args.Message.Body.ToString());
            DocumentProcessingSignalCodec.Validate(signal);
            if (!string.Equals(
                    args.Message.SessionId,
                    signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service Bus SessionId differs from the movement BusinessId.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            var result = await worker.ProcessOneAsync(signal, args.CancellationToken);
            if (string.Equals(
                    signal.DocumentType,
                    Auraly.Contracts.Sales.PosSaleDocumentTypes.Invoice,
                    StringComparison.Ordinal))
                await fiscalProcessing.RequestGenerationAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            logger.LogInformation(
                "Movement {MovementId} completed with {Result} for business {BusinessId}.",
                signal.MovementId, result, signal.BusinessId);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Movement {MovementId} failed for business {BusinessId} on delivery {DeliveryCount}.",
                signal?.MovementId, signal?.BusinessId, args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "DocumentProcessingNeedsIntervention",
                    "The ordered business stream remains blocked in SQL after five failures.",
                    args.CancellationToken);
                return;
            }

            await Task.Delay(RetryDelay, args.CancellationToken);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Service Bus processing failed for {EntityPath} from {ErrorSource}.",
            args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }
}

internal static class DocumentProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(DocumentProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static DocumentProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<DocumentProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException("The document-processing signal is invalid.");

    public static void Validate(DocumentProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.MovementId == Guid.Empty ||
            signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(signal.DocumentType) ||
            signal.DocumentType.Length > 64)
            throw new InvalidOperationException(
                "The document-processing signal has invalid identifiers or document type.");
    }
}
