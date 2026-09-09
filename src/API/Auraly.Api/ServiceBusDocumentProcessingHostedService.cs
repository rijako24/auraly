using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record DocumentProcessingServiceBusOptions(string QueueName);

public sealed class ServiceBusDocumentProcessingPublisher(
    ServiceBusSender sender,
    ILogger<ServiceBusDocumentProcessingPublisher> logger)
    : IDocumentProcessingSignalPublisher
{
    private static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(2);

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        DocumentProcessingSignalCodec.Validate(signal);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendBudget);
        try
        {
            await sender.SendMessageAsync(CreateMessage(signal), timeout.Token);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || timeout.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Service Bus did not accept movement {MovementId} within the request budget; the durable job remains pending for recovery.",
                signal.MovementId);
        }
    }

    internal static ServiceBusMessage CreateMessage(DocumentProcessingSignal signal)
    {
        var message = new ServiceBusMessage(BinaryData.FromString(
            DocumentProcessingSignalCodec.Serialize(signal)))
        {
            MessageId = signal.MovementId.ToString("D"),
            SessionId = signal.BusinessId.ToString("D"),
            Subject = signal.DocumentType,
            ContentType = "application/json"
        };
        message.ApplicationProperties["documentId"] = signal.DocumentId.ToString("D");
        return message;
    }
}

public sealed class DocumentProcessingHostedService(
    ServiceBusClient client,
    ServiceBusSender sender,
    DocumentProcessingServiceBusOptions options,
    IServiceScopeFactory scopeFactory,
    FiscalProcessingCoordinator fiscalProcessing,
    AccountingProcessingCoordinator accountingProcessing,
    SalesReportingProcessingCoordinator salesReporting,
    ILogger<DocumentProcessingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RecoverySendBudget = TimeSpan.FromSeconds(2);
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
        var recovery = RecoverSignalsAsync(stoppingToken);
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
            await IgnoreCancellation(recovery);
        }
    }

    private async Task RecoverSignalsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RecoveryInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var source = scope.ServiceProvider
                    .GetRequiredService<IDocumentProcessingWorkSource>();
                foreach (var signal in await source.ListReadySignalsAsync(100, cancellationToken))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RecoverySendBudget);
                    await sender.SendMessageAsync(
                        ServiceBusDocumentProcessingPublisher.CreateMessage(signal),
                        timeout.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Document-processing signal recovery failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
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
            if (FiscalGenerationPolicy.Supports(signal.DocumentType))
                await fiscalProcessing.RequestGenerationAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    args.CancellationToken);
            if (signal.EconomicEffectsEnabled && AccountingProcessingPolicy.Supports(signal.DocumentType))
                await accountingProcessing.RequestPostingAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    signal.DocumentType,
                    args.CancellationToken);
            if (signal.EconomicEffectsEnabled && SalesReportingProcessingPolicy.Supports(signal.DocumentType))
                await salesReporting.RequestProjectionAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    signal.DocumentType,
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
                    "DocumentProcessingDeadLettered",
                    "The movement exhausted five attempts and its ordered position was released.",
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
