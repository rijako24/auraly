using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Application.Sales;

public sealed record SalesReportingProcessingSignal(
    Guid SignalId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType);

public interface ISalesReportingProcessingSignalPublisher
{
    Task PublishAsync(
        SalesReportingProcessingSignal signal,
        CancellationToken cancellationToken = default);
}

public sealed class SalesReportingProcessingCoordinator(
    ISalesReportingProcessingSignalPublisher publisher,
    IAuralyIdGenerator ids)
{
    public Task RequestProjectionAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty ||
            !SalesReportingProcessingPolicy.Supports(documentType))
            throw new ArgumentException(
                "Business, document and a reportable document type are required.");

        return publisher.PublishAsync(
            new SalesReportingProcessingSignal(
                ids.NewId(), businessId, documentId, documentType.Trim()),
            cancellationToken);
    }
}

public static class SalesReportingProcessingPolicy
{
    public static bool Supports(string? documentType) =>
        documentType is "SalesInvoice" or "SalesReceipt" or "SalesReturn";
}
