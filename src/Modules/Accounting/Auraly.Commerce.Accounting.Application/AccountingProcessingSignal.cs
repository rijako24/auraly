using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;

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

public interface IAccountingProcessingSignalGate
{
    Task<bool> HasPendingWorkAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingProcessingCoordinator
{
    private readonly IAccountingProcessingSignalPublisher publisher;
    private readonly IAuralyIdGenerator ids;
    private readonly IAccountingProcessingSignalGate? gate;

    public AccountingProcessingCoordinator(
        IAccountingProcessingSignalPublisher publisher,
        IAuralyIdGenerator ids,
        IAccountingProcessingSignalGate gate)
    {
        this.publisher = publisher;
        this.ids = ids;
        this.gate = gate;
    }

    public AccountingProcessingCoordinator(
        IAccountingProcessingSignalPublisher publisher,
        IAuralyIdGenerator ids)
    {
        this.publisher = publisher;
        this.ids = ids;
    }

    public async Task RequestPostingAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(documentType) || documentType.Length > 64)
            throw new ArgumentException(
                "Business, document and document type are required for accounting processing.");

        if (gate is not null && !await gate.HasPendingWorkAsync(
                businessId, documentId, documentType, cancellationToken))
            return;

        await publisher.PublishAsync(
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
        "CashDisbursement",
        "Expense",
        AccountingManualDocumentTypes.AccountAdjustment,
        AccountingManualDocumentTypes.ManualVoucher,
        AccountingManualDocumentTypes.OpeningBalance
    ];


    public static bool Supports(string documentType) =>
        !string.IsNullOrWhiteSpace(documentType) && AccountableDocumentTypes.Contains(documentType);
}
