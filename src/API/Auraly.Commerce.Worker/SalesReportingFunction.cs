using Auraly.Application.Sales;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Auraly.Commerce.Worker;

public sealed class SalesReportingFunction(
    SqlSalesReportingProcessor processor,
    ILogger<SalesReportingFunction> logger)
{
    private const int MaximumDeliveries = 5;

    [Function("ProcessSalesReporting")]
    public async Task RunAsync(
        [ServiceBusTrigger("%SalesReportingQueueName%", Connection = "ServiceBusConnection",
            IsSessionsEnabled = true, AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        SalesReportingProcessingSignal? signal = null;
        try
        {
            signal = SalesReportingProcessingSignalCodec.Deserialize(message.Body.ToString());
            SalesReportingProcessingSignalCodec.Validate(signal);
            if (!string.Equals(message.SessionId, signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Service Bus SessionId differs from the reporting BusinessId.");

            await processor.ProcessAsync(signal.DocumentId, signal.DocumentType, signal.BusinessId,
                signal.SourceVersion, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Sales reporting failed for {DocumentType} {DocumentId} on delivery {DeliveryCount}.",
                signal?.DocumentType, signal?.DocumentId, message.DeliveryCount);
            if (message.DeliveryCount >= MaximumDeliveries)
                await actions.DeadLetterMessageAsync(message,
                    deadLetterReason: "SalesReportingNeedsIntervention",
                    deadLetterErrorDescription: "The sales projection failed after five deliveries.",
                    cancellationToken: cancellationToken);
            else
                await actions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
            return;
        }

        await actions.CompleteMessageAsync(message, cancellationToken);
    }
}
