using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Foundation.Tests;

public sealed class DocumentProcessingWorkerTests
{
    [Fact]
    public async Task A_message_processes_only_its_persisted_movement()
    {
        var document = CreateDocument();
        var signal = CreateSignal(document);
        var handler = new RecordingHandler();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.Ready, document)),
            new DocumentProcessingEngine(new ReceiptStore(), [handler]));

        var result = await worker.ProcessOneAsync(signal);

        Assert.Equal(DocumentProcessingResult.Processed, result);
        Assert.Equal([document.DocumentId], handler.Completed);
    }

    [Fact]
    public async Task A_later_message_is_not_acknowledged_before_the_previous_movement()
    {
        var document = CreateDocument();
        var handler = new RecordingHandler();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(
                DocumentProcessingWorkState.NotReady,
                null,
                "An earlier movement must complete first.")),
            new DocumentProcessingEngine(new ReceiptStore(), [handler]));

        var error = await Assert.ThrowsAsync<DocumentProcessingMessageException>(
            () => worker.ProcessOneAsync(CreateSignal(document)));

        Assert.Contains("earlier movement", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Completed);
    }

    [Fact]
    public async Task A_duplicate_message_for_a_completed_movement_is_idempotent()
    {
        var document = CreateDocument();
        var handler = new RecordingHandler();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.Completed, null)),
            new DocumentProcessingEngine(new ReceiptStore(), [handler]));

        var result = await worker.ProcessOneAsync(CreateSignal(document));

        Assert.Equal(DocumentProcessingResult.AlreadyProcessed, result);
        Assert.Empty(handler.Completed);
    }

    [Fact]
    public async Task A_terminal_failure_never_dispatches_derived_work()
    {
        var document = CreateDocument();
        var handler = new RecordingHandler();
        var observer = new RecordingObserver();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.DeadLettered, null)),
            new DocumentProcessingEngine(new ReceiptStore(), [handler]), [observer]);

        Assert.Equal(DocumentProcessingResult.DeadLettered,
            await worker.ProcessOneAsync(CreateSignal(document)));
        Assert.Empty(handler.Completed);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task A_replay_with_no_pending_effects_returns_without_dispatching_again()
    {
        var document = CreateDocument();
        var observer = new RecordingObserver();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.CompletedAndPublished, null)),
            new DocumentProcessingEngine(new ReceiptStore(), [new RecordingHandler()]), [observer]);

        Assert.Equal(DocumentProcessingResult.AlreadyProcessed,
            await worker.ProcessOneAsync(CreateSignal(document)));
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task Persisted_habilitation_mode_is_authoritative_when_the_broker_signal_differs()
    {
        var document = CreateDocument();
        var observer = new RecordingObserver();
        var worker = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.Completed, null, EconomicEffectsEnabled: false)),
            new DocumentProcessingEngine(new ReceiptStore(), [new RecordingHandler()]), [observer]);

        await worker.ProcessOneAsync(CreateSignal(document) with { EconomicEffectsEnabled = true });

        Assert.False(Assert.Single(observer.Signals).EconomicEffectsEnabled);
    }

    [Fact]
    public async Task Completion_observers_run_for_processed_and_replayed_messages()
    {
        var document = CreateDocument(); var observer = new RecordingObserver();
        var processed = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.Ready, document)),
            new DocumentProcessingEngine(new ReceiptStore(), [new RecordingHandler()]), [observer]);
        await processed.ProcessOneAsync(CreateSignal(document));
        var replayed = new DocumentProcessingWorker(
            new WorkSource(new(DocumentProcessingWorkState.Completed, null)),
            new DocumentProcessingEngine(new ReceiptStore(), [new RecordingHandler()]), [observer]);
        await replayed.ProcessOneAsync(CreateSignal(document));
        Assert.Equal(2, observer.Calls);
    }

    private sealed class RecordingObserver : IDocumentProcessingCompletionObserver
    {
        public List<DocumentProcessingSignal> Signals { get; } = [];
        public int Calls => Signals.Count;

        public Task ObserveAsync(DocumentProcessingSignal signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal);
            return Task.CompletedTask;
        }
    }

    private static DocumentProcessingSignal CreateSignal(ConfirmedDocument document) =>
        new(Guid.NewGuid(), document.BusinessId.Value, document.DocumentId.Value, document.DocumentType);

    private static ConfirmedDocument CreateDocument() =>
        new(
            new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            new DocumentId(Guid.NewGuid()),
            RecordingHandler.Type,
            "{}",
            DateTimeOffset.UtcNow);

    private sealed class WorkSource(DocumentProcessingWork work)
        : IDocumentProcessingWorkSource
    {
        public Task<IReadOnlyList<DocumentProcessingSignal>> ListReadySignalsAsync(
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentProcessingSignal>>([]);

        public Task<DocumentProcessingWork> LoadAsync(
            DocumentProcessingSignal signal,
            CancellationToken cancellationToken) => Task.FromResult(work);
    }

    private sealed class RecordingHandler : IConfirmedDocumentHandler
    {
        public const string Type = "sales.invoice";
        public List<DocumentId> Completed { get; } = [];
        string IConfirmedDocumentHandler.DocumentType => Type;

        public Task HandleAsync(
            ConfirmedDocument document,
            CancellationToken cancellationToken)
        {
            Completed.Add(document.DocumentId);
            return Task.CompletedTask;
        }
    }

    private sealed class ReceiptStore : IDocumentProcessingJobStore
    {
        private readonly ConcurrentDictionary<DocumentId, string> _states = new();

        public Task<ProcessingLeaseResult> TryAcquireAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _states.TryAdd(context.DocumentId, "Processing")
                    ? ProcessingLeaseResult.Acquired
                    : ProcessingLeaseResult.Busy);

        public Task MarkCompletedAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken)
        {
            _states[context.DocumentId] = "Completed";
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            DocumentProcessingContext context,
            Exception error,
            CancellationToken cancellationToken)
        {
            _states[context.DocumentId] = "Failed";
            return Task.CompletedTask;
        }
    }
}
