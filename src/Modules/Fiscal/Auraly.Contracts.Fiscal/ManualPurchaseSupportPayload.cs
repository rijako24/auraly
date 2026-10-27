using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Contracts.Fiscal;

// Immutable acquisition evidence supplied by the manual journal. This payload
// does not impersonate an expense, physical receipt or commercial product.
public sealed record ManualPurchaseSupportLineSnapshot(
    int LineNumber, Guid SourceLineId, string Description,
    decimal TaxExclusiveAmount, decimal VatAmount, decimal TaxRate);

public sealed record ManualPurchaseSupportPayload(
    Guid TenantId, Guid BusinessId, Guid DocumentId, Guid SupplierId,
    DateTimeOffset IssuedAt, DateTimeOffset DueDate, string CurrencyCode,
    IReadOnlyList<ManualPurchaseSupportLineSnapshot> Lines,
    WithholdingCalculationSnapshot Withholding);

// A cancellation preserves the accepted acquisition and its own identity/date.
// It is not an expense cancellation or a physical purchase return.
public sealed record ManualPurchaseSupportCancellationPayload(
    Guid TenantId, Guid BusinessId, Guid CancellationId, DateTimeOffset CancelledAt,
    ManualPurchaseSupportPayload Original);
