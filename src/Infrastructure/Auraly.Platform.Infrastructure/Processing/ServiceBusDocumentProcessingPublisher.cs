using System.Collections.Concurrent;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Infrastructure.Processing;

public sealed class ServiceBusDocumentProcessingPublisher(
    ServiceBusSender sender,
    ILogger<ServiceBusDocumentProcessingPublisher> logger,
    IDocumentInitialSignalPublicationStore activation,
    IDocumentContinuationSignalPublicationStore? continuations = null)
    : IDocumentProcessingSignalPublisher
{
    private static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ContinuationBatchBudget = TimeSpan.FromSeconds(90);

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        DocumentProcessingSignalCodec.Validate(signal);
        await PublishClaimedAsync(signal, signal.BusinessId, 1, cancellationToken);
    }

    public Task RecoverInitialSignalsAsync(CancellationToken cancellationToken) =>
        PublishClaimedAsync(null, null, 100, cancellationToken);

    public async Task RecoverContinuationSignalsAsync(CancellationToken cancellationToken)
    {
        if (continuations is null)
            return;

        var claim = await continuations.ClaimAsync(cancellationToken);
        if (claim.Activations.Count == 0)
            return;

        var acknowledged = new ConcurrentBag<Guid>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ContinuationBatchBudget);
        try
        {
            await Parallel.ForEachAsync(claim.Activations.GroupBy(item => item.Signal.BusinessId), new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = deadline.Token
            }, async (business, token) =>
            {
                foreach (var item in business)
                {
                    var message = CreateMessage(item.Signal);
                    message.MessageId = FormattableString.Invariant(
                        $"{item.OutboxMessageId:D}:continuation:{item.PublicationCount}");
                    if (!await SendAsync(message, token))
                        break;
                    acknowledged.Add(item.OutboxMessageId);
                }
            });
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Document continuation recovery reached its send budget with {Published} confirmed.",
                acknowledged.Count);
        }
        await continuations.MarkPublishedAsync(claim.LeaseId, acknowledged.ToArray(), cancellationToken);
        logger.LogInformation("Document continuation recovery claimed {Claimed} signals and published {Published}.",
            claim.Activations.Count, acknowledged.Count);
    }

    public Task PublishNextAsync(Guid businessId, CancellationToken cancellationToken) =>
        PublishClaimedAsync(null, businessId, 1, cancellationToken);

    public async Task ScheduleRetryAsync(
        DocumentProcessingSignal signal,
        DocumentProcessingRetry retry,
        CancellationToken cancellationToken)
    {
        var message = CreateActivationMessage(new(signal, retry.FailedAttempts));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendBudget);
        await sender.ScheduleMessageAsync(message, retry.AvailableAt, timeout.Token);
    }

    private async Task PublishClaimedAsync(
        DocumentProcessingSignal? requested,
        Guid? businessId,
        int maximum,
        CancellationToken cancellationToken)
    {
        if (requested is not null)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(SendBudget);
            try
            {
                await ClaimAndSendAsync(requested, businessId, maximum, deadline.Token);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Movement {MovementId} was committed and its activation remains eligible for recovery.",
                    requested.MovementId);
            }
            return;
        }

        await ClaimAndSendAsync(null, businessId, maximum, cancellationToken);
    }

    private async Task ClaimAndSendAsync(
        DocumentProcessingSignal? requested,
        Guid? businessId,
        int maximum,
        CancellationToken cancellationToken)
    {
        var claim = await activation.ClaimAsync(requested, businessId, maximum, cancellationToken);
        if (claim.Signals.Count == 0)
            return;

        var acknowledged = new ConcurrentBag<Guid>();
        // SQL claims at most one movement per business, so separate sessions can
        // publish concurrently while every business retains its durable order.
        await Parallel.ForEachAsync(claim.Signals, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (item, token) =>
        {
            if (await SendAsync(CreateActivationMessage(item), token))
                acknowledged.Add(item.Signal.MovementId);
        });

        await activation.MarkPublishedAsync(claim.LeaseId, acknowledged.ToArray(), cancellationToken);
        if (requested is null && businessId is null)
            logger.LogInformation("Document activation claimed {Claimed} signals and published {Published}.",
                claim.Signals.Count, acknowledged.Count);
    }

    private static ServiceBusMessage CreateActivationMessage(DocumentInitialActivation item)
    {
        var message = CreateMessage(item.Signal);
        if (item.FailedAttempts > 0)
            message.MessageId = FormattableString.Invariant($"{item.Signal.MovementId:D}:retry:{item.FailedAttempts}");
        return message;
    }

    private async Task<bool> SendAsync(ServiceBusMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendBudget);
        try
        {
            await sender.SendMessageAsync(message, timeout.Token);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Service Bus did not acknowledge document message {MessageId} within the request budget.",
                message.MessageId);
            return false;
        }
    }

    public static ServiceBusMessage CreateMessage(DocumentProcessingSignal signal)
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

public static class DocumentProcessingSignalCodec
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
