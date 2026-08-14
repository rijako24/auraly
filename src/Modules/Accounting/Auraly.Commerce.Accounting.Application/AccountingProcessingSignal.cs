using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Commerce.Accounting.Application;

public sealed record AccountingProcessingSignal(
    Guid SignalId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType);

public interface IAccountingProcessingSignalPublisher
{
    Task PublishAsync(
        AccountingProcessingSignal signal,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingProcessingCoordinator(
    IAccountingProcessingSignalPublisher publisher,
    IAuralyIdGenerator ids)
{
    public Task RequestPostingAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(documentType) || documentType.Length > 64)
            throw new ArgumentException(
                "Business, document and document type are required for accounting processing.");

        return publisher.PublishAsync(
            new AccountingProcessingSignal(
                ids.NewId(), businessId, documentId, documentType.Trim()),
            cancellationToken);
    }
}

public static class AccountingProcessingPolicy
{
    private static readonly HashSet<string> AccountableDocumentTypes =
    [
        "SalesInvoice",
        "SalesReceipt",
        "SalesReturn",
        "GoodsReceipt",
        "PurchaseReturn",
        "PayablePayment",
        "ReceivablePayment",
        "CashReceipt",
        "CashDisbursement"
    ];


    public static bool Supports(string documentType) =>
        !string.IsNullOrWhiteSpace(documentType) && AccountableDocumentTypes.Contains(documentType);
}
