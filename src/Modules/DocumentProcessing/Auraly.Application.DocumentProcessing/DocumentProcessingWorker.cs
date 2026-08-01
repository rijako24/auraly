using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Application.DocumentProcessing;

public sealed record DocumentProcessingSignal(
    Guid MovementId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType);

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
    Missing
}

public sealed record DocumentProcessingWork(
    DocumentProcessingWorkState State,
    ConfirmedDocument? Document,
    string? Reason = null);

public interface IDocumentProcessingWorkSource
{
    Task<DocumentProcessingWork> LoadAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken);
}

public sealed class DocumentProcessingMessageException(string message) : Exception(message);

public sealed class DocumentProcessingWorker(
    IDocumentProcessingWorkSource workSource,
    DocumentProcessingEngine engine)
{
    public async Task<DocumentProcessingResult> ProcessOneAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var work = await workSource.LoadAsync(signal, cancellationToken);
        if (work.State == DocumentProcessingWorkState.Completed)
            return DocumentProcessingResult.AlreadyProcessed;
        if (work.State != DocumentProcessingWorkState.Ready || work.Document is null)
            throw new DocumentProcessingMessageException(
                work.Reason ?? "The movement is missing or cannot be processed in sequence.");

        var result = await engine.ProcessAsync(work.Document, cancellationToken);
        if (result == DocumentProcessingResult.Busy)
            throw new DocumentProcessingMessageException(
                "The movement is already leased by another consumer.");
        return result;
    }
}
