using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Dispatching;

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
    Task<IReadOnlyList<AccountingPendingWork>> ListPendingWorkAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default);
}

public sealed record AccountingPendingWork(Guid DocumentId, string DocumentType);

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

        var pending = gate is null
            ? [new AccountingPendingWork(documentId, documentType.Trim())]
            : await gate.ListPendingWorkAsync(
                businessId, documentId, documentType.Trim(), cancellationToken);
        foreach (var work in pending)
            await publisher.PublishAsync(
                new AccountingProcessingSignal(
                    ids.NewId(), businessId, work.DocumentId, work.DocumentType),
                cancellationToken);
    }
}

public static class AccountingProcessingPolicy
{
    private static readonly HashSet<string> AccountableDocumentTypes =
    [
        "SalesInvoice",
        "ServiceInvoice",
        "SalesReceipt",
        "SalesReturn",
        "SalesDebitNote",
        "GoodsReceipt",
        "GoodsReceiptCostDocument",
        "PurchaseReturn",
        "PayablePayment",
        "ReceivablePayment",
        "CashReceipt",
        "CashDisbursement",
        "Expense",
        AccountingManualDocumentTypes.AccountAdjustment,
        AccountingManualDocumentTypes.ManualVoucher,
        AccountingManualDocumentTypes.OpeningBalance,
        Auraly.Contracts.WorkSessions.WorkSessionAccountingDocumentTypes.CashDifference,
        Auraly.Contracts.WorkSessions.WorkSessionAccountingDocumentTypes.ClosureReconciliation,
        PayrollAccountingDocumentTypes.Accrual,
        PayrollAccountingDocumentTypes.Payment,
        PayrollAccountingDocumentTypes.Adjustment,
        InventoryDocumentTypes.StockCount,
        InventoryDocumentTypes.Adjustment,
        InventoryDocumentTypes.Damage,
        InventoryDocumentTypes.Conversion,
        InventoryDocumentTypes.TransferReceipt,
        DispatchAccountingDocumentTypes.CashDifference
    ];


    public static bool Supports(string documentType) =>
        !string.IsNullOrWhiteSpace(documentType) && AccountableDocumentTypes.Contains(documentType);
}
