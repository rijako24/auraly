namespace Auraly.Application.DocumentProcessing;

public sealed record DocumentContinuationActivation(
    Guid OutboxMessageId, DocumentProcessingSignal Signal, int PublicationCount);

public sealed record DocumentContinuationClaim(
    Guid LeaseId, IReadOnlyList<DocumentContinuationActivation> Activations);

public interface IDocumentContinuationSignalPublicationStore
{
    Task<DocumentContinuationClaim> ClaimAsync(CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid leaseId, IReadOnlyCollection<Guid> outboxMessageIds, CancellationToken cancellationToken);
}
