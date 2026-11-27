using Auraly.Platform.Infrastructure.Processing;
using Auraly.Application.DocumentProcessing;
using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class ServiceBusDocumentProcessingPublisherTests
{
    [Fact]
    public async Task Recovery_marks_only_signals_accepted_by_the_broker()
    {
        var sent = new DocumentProcessingSignal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "GoodsReceipt");
        var interrupted = new DocumentProcessingSignal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SalesReceipt");
        var activation = new RecordingActivationStore([sent, interrupted]);
        var sender = new RecordingServiceBusSender(interrupted.MovementId);
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender, NullLogger<ServiceBusDocumentProcessingPublisher>.Instance, activation);

        await publisher.RecoverInitialSignalsAsync(default);

        Assert.Equal([sent.MovementId], activation.Acknowledged);
        Assert.Equal(2, sender.Sent.Count);
        Assert.Contains(sender.Sent, message => message.MessageId == sent.MovementId.ToString("D")
            && message.SessionId == sent.BusinessId.ToString("D"));
    }

    [Fact]
    public async Task Continuation_publication_reuses_the_broker_identity_until_send_is_confirmed()
    {
        var signal = new DocumentProcessingSignal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SalesReceipt");
        var activity = new DocumentContinuationActivation(Guid.NewGuid(), signal, 0);
        var store = new RecordingContinuationStore([activity]);
        var sender = new RecordingServiceBusSender(Guid.Empty);
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender, NullLogger<ServiceBusDocumentProcessingPublisher>.Instance, continuations: store);

        await publisher.RecoverContinuationSignalsAsync(default);
        await publisher.RecoverContinuationSignalsAsync(default);
        store.Activations = [activity with { PublicationCount = 1 }];
        await publisher.RecoverContinuationSignalsAsync(default);

        Assert.Equal([activity.OutboxMessageId], store.Acknowledged);
        var groups = sender.Sent.GroupBy(message => message.MessageId).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(2, groups[$"{activity.OutboxMessageId:D}:continuation:0"]);
        Assert.Equal(1, groups[$"{activity.OutboxMessageId:D}:continuation:1"]);
        Assert.Equal(2, groups.Count);
        Assert.All(sender.Sent, message =>
        {
            Assert.Equal(signal.BusinessId.ToString("D"), message.SessionId);
            Assert.Equal(signal, DocumentProcessingSignalCodec.Deserialize(message.Body.ToString()));
        });
    }

    [Fact]
    public async Task A_failed_recovery_keeps_the_rest_of_its_session_pending_and_allows_other_sessions()
    {
        var first = new DocumentProcessingSignal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SalesReceipt");
        var next = first with { MovementId = Guid.NewGuid(), DocumentId = Guid.NewGuid() };
        var otherBusiness = next with { MovementId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), BusinessId = Guid.NewGuid() };
        var interrupted = new DocumentContinuationActivation(Guid.NewGuid(), first, 0);
        var sameSession = new DocumentContinuationActivation(Guid.NewGuid(), next, 0);
        var independent = new DocumentContinuationActivation(Guid.NewGuid(), otherBusiness, 0);
        var store = new RecordingContinuationStore([interrupted, sameSession, independent]);
        var sender = new RecordingServiceBusSender(Guid.Empty, $"{interrupted.OutboxMessageId:D}:continuation:0");
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender, NullLogger<ServiceBusDocumentProcessingPublisher>.Instance, continuations: store);

        await publisher.RecoverContinuationSignalsAsync(default);

        Assert.Equal([independent.OutboxMessageId], store.Acknowledged);
        Assert.Equal(2, sender.Sent.Count);
        Assert.DoesNotContain(sender.Sent, message => message.MessageId.StartsWith(sameSession.OutboxMessageId.ToString("D")));
    }

    [Fact]
    public async Task Document_retry_has_one_stable_broker_identity_per_persisted_attempt()
    {
        var signal = new DocumentProcessingSignal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "GoodsReceipt");
        var sender = new RecordingServiceBusSender(Guid.Empty);
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender, NullLogger<ServiceBusDocumentProcessingPublisher>.Instance);
        var due = DateTimeOffset.UtcNow.AddSeconds(2);

        await publisher.ScheduleRetryAsync(signal, new DocumentProcessingRetry(2, due), default);
        await publisher.ScheduleRetryAsync(signal, new DocumentProcessingRetry(2, due), default);
        await publisher.ScheduleRetryAsync(signal, new DocumentProcessingRetry(3, due), default);

        var messages = sender.Scheduled.ToArray();
        Assert.Equal(messages[0].MessageId, messages[1].MessageId);
        Assert.NotEqual(messages[1].MessageId, messages[2].MessageId);
        Assert.All(messages, message => Assert.Equal(signal, DocumentProcessingSignalCodec.Deserialize(message.Body.ToString())));
    }

    [Fact]
    public async Task Publishing_accepts_the_committed_signal_without_waiting_for_the_network_sender()
    {
        var sender = new BlockingServiceBusSender();
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender,
            NullLogger<ServiceBusDocumentProcessingPublisher>.Instance);
        var signal = new DocumentProcessingSignal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "GoodsReceipt");

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await publisher.PublishAsync(signal);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3),
            $"The durable request path waited {elapsed.Elapsed} before returning.");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(1.5),
            $"The blocking sender was not held to the configured budget: {elapsed.Elapsed}.");
        Assert.Equal(1, sender.Calls);
    }

    private sealed class BlockingServiceBusSender : ServiceBusSender
    {
        public int Calls { get; private set; }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RecordingActivationStore(IReadOnlyList<DocumentProcessingSignal> pending)
        : IDocumentInitialSignalPublicationStore
    {
        private readonly Guid leaseId = Guid.NewGuid();

        public IReadOnlyCollection<Guid> Acknowledged { get; private set; } = [];

        public Task<DocumentInitialSignalClaim> ClaimAsync(
            DocumentProcessingSignal? requested, Guid? businessId, int maximum, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentInitialSignalClaim(leaseId, pending));

        public Task MarkPublishedAsync(
            Guid lease, IReadOnlyCollection<Guid> movementIds, CancellationToken cancellationToken)
        {
            Assert.Equal(leaseId, lease);
            Acknowledged = movementIds;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContinuationStore(IReadOnlyList<DocumentContinuationActivation> activations)
        : IDocumentContinuationSignalPublicationStore
    {
        private readonly Guid leaseId = Guid.NewGuid();
        public IReadOnlyList<DocumentContinuationActivation> Activations { get; set; } = activations;
        public IReadOnlyCollection<Guid> Acknowledged { get; private set; } = [];

        public Task<DocumentContinuationClaim> ClaimAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentContinuationClaim(leaseId, Activations));

        public Task MarkPublishedAsync(Guid lease, IReadOnlyCollection<Guid> outboxMessageIds,
            CancellationToken cancellationToken)
        {
            Assert.Equal(leaseId, lease);
            Acknowledged = outboxMessageIds;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingServiceBusSender(Guid failMovement, string? failMessageId = null) : ServiceBusSender
    {
        public ConcurrentBag<ServiceBusMessage> Sent { get; } = [];
        public ConcurrentQueue<ServiceBusMessage> Scheduled { get; } = new();

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return message.MessageId == failMovement.ToString("D") || message.MessageId == failMessageId
                ? Task.FromException(new IOException("The broker did not confirm the send."))
                : Task.CompletedTask;
        }

        public override Task<long> ScheduleMessageAsync(
            ServiceBusMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken cancellationToken = default)
        {
            Scheduled.Enqueue(message);
            return Task.FromResult((long)Scheduled.Count);
        }
    }
}
