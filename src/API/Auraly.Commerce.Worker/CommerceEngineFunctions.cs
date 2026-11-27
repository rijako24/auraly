using Auraly.Application.DocumentProcessing;
using Auraly.Application.Dispatching;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Dispatching;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Auraly.Commerce.Worker;

public sealed class CommerceEngineFunctions(
    DocumentProcessingWorker documents,
    IDocumentProcessingWorkSource documentWork,
    SqlAccountingPostingProcessor accounting,
    DispatchSettlementPreparation dispatches,
    FiscalProcessingMessageHandler fiscal,
    SqlPosSynchronizationOutboxDispatcher synchronization,
    ServiceBusDocumentProcessingPublisher documentPublisher,
    ILogger<CommerceEngineFunctions> logger)
{
    private const int InterventionDelivery = 5;

    [Function("ProcessDocument")]
    public async Task ProcessDocumentAsync(
        [ServiceBusTrigger("%DocumentProcessingQueueName%", Connection = "ServiceBusConnection",
            IsSessionsEnabled = true, AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        DocumentProcessingSignal signal;
        try
        {
            signal = DocumentProcessingSignalCodec.Deserialize(message.Body.ToString());
            DocumentProcessingSignalCodec.Validate(signal);
            ValidateSession(message, signal.BusinessId);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException
            or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Invalid document message {MessageId}.", message.MessageId);
            await actions.DeadLetterMessageAsync(message,
                deadLetterReason: "InvalidDocumentSignal",
                cancellationToken: cancellationToken);
            return;
        }

        DocumentProcessingResult result;
        try
        {
            result = await documents.ProcessOneAsync(signal, cancellationToken);
            if (result != DocumentProcessingResult.DeadLettered &&
                signal.DocumentType == PurchasingDocumentTypes.GoodsReceipt)
                await synchronization.PublishBusinessAsync(signal.BusinessId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Document message {MessageId} failed on delivery {DeliveryCount}.",
                message.MessageId, message.DeliveryCount);
            var work = await documentWork.LoadAsync(signal, cancellationToken);
            if (work.State == DocumentProcessingWorkState.DeadLettered)
            {
                await documentPublisher.PublishNextAsync(signal.BusinessId, cancellationToken);
                await actions.DeadLetterMessageAsync(message,
                    deadLetterReason: "DocumentProcessingDeadLettered",
                    deadLetterErrorDescription: "The movement is terminal in SQL.",
                    cancellationToken: cancellationToken);
            }
            else if (work.Retry is { } retry)
            {
                var authoritativeSignal = signal with
                {
                    EconomicEffectsEnabled = work.EconomicEffectsEnabled ?? signal.EconomicEffectsEnabled
                };
                await documentPublisher.ScheduleRetryAsync(authoritativeSignal, retry, cancellationToken);
                await actions.CompleteMessageAsync(message, cancellationToken);
            }
            else
            {
                // Broker deliveries can increase while the job is waiting on its predecessor.
                await actions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
            }
            return;
        }

        await documentPublisher.PublishNextAsync(signal.BusinessId, cancellationToken);
        if (result == DocumentProcessingResult.DeadLettered)
            await actions.DeadLetterMessageAsync(message,
                deadLetterReason: "DocumentProcessingDeadLettered",
                deadLetterErrorDescription: "The movement is terminal in SQL.",
                cancellationToken: cancellationToken);
        else
            await actions.CompleteMessageAsync(message, cancellationToken);
    }

    [Function("ProcessAccounting")]
    public async Task ProcessAccountingAsync(
        [ServiceBusTrigger("%AccountingProcessingQueueName%", Connection = "ServiceBusConnection",
            IsSessionsEnabled = true, AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        try
        {
            if (message.Subject == DispatchSettlementQueue.Subject)
            {
                var dispatch = DispatchSettlementSignalCodec.Deserialize(message.Body.ToString());
                DispatchSettlementSignalCodec.Validate(dispatch);
                ValidateSession(message, dispatch.BusinessId);
                await dispatches.ProcessAsync(dispatch, cancellationToken);
            }
            else
            {
                var signal = AccountingProcessingSignalCodec.Deserialize(message.Body.ToString());
                AccountingProcessingSignalCodec.Validate(signal);
                ValidateSession(message, signal.BusinessId);
                await accounting.ProcessAsync(signal.DocumentId, signal.DocumentType, signal.BusinessId,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await SettleFailedDeliveryAsync(message, actions, "AccountingProcessingNeedsIntervention",
                exception, cancellationToken);
            return;
        }

        await actions.CompleteMessageAsync(message, cancellationToken);
    }

    [Function("ProcessFiscal")]
    public async Task ProcessFiscalAsync(
        [ServiceBusTrigger("%FiscalProcessingQueueName%", Connection = "ServiceBusConnection",
            IsSessionsEnabled = true, AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var signal = FiscalProcessingSignalCodec.Deserialize(message.Body.ToString());
            FiscalProcessingSignalCodec.Validate(signal);
            ValidateSession(message, signal.BusinessId);
            await fiscal.ProcessAsync(signal, context.InvocationId, cancellationToken);
            await synchronization.PublishBusinessAsync(signal.BusinessId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await SettleFailedDeliveryAsync(message, actions, "FiscalProcessingNeedsIntervention",
                exception, cancellationToken);
            return;
        }

        await actions.CompleteMessageAsync(message, cancellationToken);
    }

    private static void ValidateSession(ServiceBusReceivedMessage message, Guid businessId)
    {
        if (!Guid.TryParseExact(message.SessionId, "D", out var sessionBusiness) || sessionBusiness != businessId)
            throw new InvalidOperationException("Service Bus SessionId differs from the persisted work scope.");
    }

    private async Task SettleFailedDeliveryAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        string reason,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Engine message {MessageId} failed on delivery {DeliveryCount}.",
            message.MessageId, message.DeliveryCount);
        if (message.DeliveryCount >= InterventionDelivery)
            await actions.DeadLetterMessageAsync(message,
                deadLetterReason: reason, cancellationToken: cancellationToken);
        else
            await actions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
    }
}
