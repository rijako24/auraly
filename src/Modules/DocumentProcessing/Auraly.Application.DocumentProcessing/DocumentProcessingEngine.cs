using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Application.DocumentProcessing;

public enum ProcessingLeaseResult
{
    Acquired,
    AlreadyCompleted,
    Busy
}

public sealed record DocumentProcessingContext(
    TenantId TenantId,
    BusinessId BusinessId,
    DocumentId DocumentId,
    string DocumentType);

public interface IDocumentProcessingReceiptStore
{
    Task<ProcessingLeaseResult> TryAcquireAsync(
        DocumentProcessingContext context,
        CancellationToken cancellationToken);

    Task MarkCompletedAsync(
        DocumentProcessingContext context,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        DocumentProcessingContext context,
        Exception error,
        CancellationToken cancellationToken);
}

public interface IConfirmedDocumentHandler
{
    string DocumentType { get; }

    Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken);
}

public enum DocumentProcessingResult
{
    Processed,
    AlreadyProcessed,
    Busy
}

public sealed class DocumentProcessingEngine(
    IDocumentProcessingReceiptStore receiptStore,
    IEnumerable<IConfirmedDocumentHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IConfirmedDocumentHandler> _handlers =
        handlers.ToDictionary(handler => handler.DocumentType, StringComparer.Ordinal);

    public async Task<DocumentProcessingResult> ProcessAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_handlers.TryGetValue(document.DocumentType, out var handler))
        {
            throw new InvalidOperationException($"No handler is registered for '{document.DocumentType}'.");
        }

        var context = new DocumentProcessingContext(
            document.TenantId,
            document.BusinessId,
            document.DocumentId,
            document.DocumentType);

        var lease = await receiptStore.TryAcquireAsync(context, cancellationToken);
        if (lease == ProcessingLeaseResult.AlreadyCompleted)
        {
            return DocumentProcessingResult.AlreadyProcessed;
        }

        if (lease == ProcessingLeaseResult.Busy)
        {
            return DocumentProcessingResult.Busy;
        }

        try
        {
            await handler.HandleAsync(document, cancellationToken);
            await receiptStore.MarkCompletedAsync(context, cancellationToken);
            return DocumentProcessingResult.Processed;
        }
        catch (Exception exception)
        {
            await receiptStore.MarkFailedAsync(context, exception, cancellationToken);
            throw;
        }
    }
}
