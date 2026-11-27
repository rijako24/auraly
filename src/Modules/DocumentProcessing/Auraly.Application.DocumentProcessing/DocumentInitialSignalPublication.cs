namespace Auraly.Application.DocumentProcessing;

public sealed record DocumentInitialSignalClaim(
    Guid LeaseId,
    IReadOnlyList<DocumentProcessingSignal> Signals);

public interface IDocumentInitialSignalPublicationStore
{
    Task<DocumentInitialSignalClaim> ClaimAsync(
        DocumentProcessingSignal? requested,
        Guid? businessId,
        int maximum,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid leaseId,
        IReadOnlyCollection<Guid> movementIds,
        CancellationToken cancellationToken);
}
