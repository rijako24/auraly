using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Application.DocumentProcessing;

public sealed record DocumentProcessingSignal(
    Guid MovementId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType,
    bool EconomicEffectsEnabled = true);

public interface IDocumentProcessingSignalPublisher
{
    Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default);
}

public enum DocumentProcessingWorkState
{
    Ready,
    Completed,
    NotReady,
    Missing,
    DeadLettered,
    CompletedAndPublished
}

public sealed record DocumentProcessingWork(
    DocumentProcessingWorkState State,
    ConfirmedDocument? Document,
    string? Reason = null,
    bool? EconomicEffectsEnabled = null,
    DocumentProcessingRetry? Retry = null);

public sealed record DocumentProcessingRetry(int FailedAttempts, DateTimeOffset AvailableAt);

public interface IDocumentProcessingWorkSource
{
    Task<IReadOnlyList<DocumentProcessingSignal>> ListReadySignalsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<DocumentProcessingWork> LoadAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken);
}

public interface IDocumentProcessingCompletionObserver
{
    Task ObserveAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken);
}

public sealed class DocumentProcessingMessageException(string message) : Exception(message);

public sealed class DocumentProcessingWorker(
    IDocumentProcessingWorkSource workSource,
    DocumentProcessingEngine engine,
    IEnumerable<IDocumentProcessingCompletionObserver>? completionObservers = null)
{
    public async Task<DocumentProcessingResult> ProcessOneAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var work = await workSource.LoadAsync(signal, cancellationToken);
        if (work.State == DocumentProcessingWorkState.DeadLettered)
            return DocumentProcessingResult.DeadLettered;
        if (work.State == DocumentProcessingWorkState.CompletedAndPublished)
            return DocumentProcessingResult.AlreadyProcessed;

        if (work.EconomicEffectsEnabled is { } savedMode)
            signal = signal with { EconomicEffectsEnabled = savedMode };
        if (work.State == DocumentProcessingWorkState.Completed)
        {
            await ObserveCompletionAsync(signal, cancellationToken);
            return DocumentProcessingResult.AlreadyProcessed;
        }
        if (work.State != DocumentProcessingWorkState.Ready || work.Document is null)
            throw new DocumentProcessingMessageException(
                work.Reason ?? "The movement is missing or cannot be processed in sequence.");

        var result = await engine.ProcessAsync(work.Document, cancellationToken);
        if (result == DocumentProcessingResult.Busy)
            throw new DocumentProcessingMessageException(
                "The movement is already leased by another consumer.");
        if (result != DocumentProcessingResult.DeadLettered)
            await ObserveCompletionAsync(signal, cancellationToken);
        return result;
    }

    private async Task ObserveCompletionAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        if (completionObservers is null) return;
        foreach (var observer in completionObservers)
            await observer.ObserveAsync(signal, cancellationToken);
    }
}
