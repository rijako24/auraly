using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Application.Sales;

public sealed record SalesReportingProcessingSignal(
    Guid SignalId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType,
    long SourceVersion = 1);

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
        CancellationToken cancellationToken = default,
        long sourceVersion = 1)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty ||
            sourceVersion <= 0 || !SalesReportingProcessingPolicy.Supports(documentType))
            throw new ArgumentException(
                "Business, document and a reportable document type are required.");

        return publisher.PublishAsync(
            new SalesReportingProcessingSignal(
                ids.NewId(), businessId, documentId, documentType.Trim(),sourceVersion),
            cancellationToken);
    }
}

public static class SalesReportingProcessingPolicy
{
    public static bool Supports(string? documentType) =>
        documentType is "SalesInvoice" or "SalesReceipt" or "ServiceInvoice" or "SalesReturn" or "RouteVisit" or
            "SellerOrder" or "CommercialCoveragePlan" or "GoodsReceipt" or "PurchaseReturn";
}
