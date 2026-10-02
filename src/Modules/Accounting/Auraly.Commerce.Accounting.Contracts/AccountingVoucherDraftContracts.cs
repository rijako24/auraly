namespace Auraly.Commerce.Accounting.Contracts;

public sealed record VoucherDraftLine(
    Guid? AccountId, Guid? PartyId, Guid? CostCenterId, string Description,
    decimal Debit, decimal Credit, string? Reference = null);

public sealed record VoucherDraftAdjustment(
    string SubledgerKind, Guid SubledgerId, string Direction, decimal Amount,
    Guid? CounterpartAccountId, Guid? CostCenterId);

public sealed record SaveVoucherDraftRequest(
    Guid DocumentId, string DocumentType, DateTimeOffset OccurredAt,
    string ConceptCode, string Description, string? Reference,
    IReadOnlyList<VoucherDraftLine> Lines, VoucherDraftAdjustment? Adjustment,
    string? RowVersion);

public sealed record SendVoucherDraftRequest(string RowVersion);

public sealed record VoucherDraftLineView(
    VoucherDraftLine Value, string? AccountCode, string? AccountName,
    string? PartyName, string? PartyIdentification, string? CostCenterName);

public sealed record VoucherDraftView(
    Guid DocumentId, string DocumentType, DateTimeOffset OccurredAt,
    string ConceptCode, string Description, string? Reference, string CurrencyCode,
    IReadOnlyList<VoucherDraftLineView> Lines, VoucherDraftAdjustment? Adjustment,
    string RowVersion, DateTimeOffset? SentAt, string Status,
    bool CanEdit, bool CanSend, AccountingDocumentRow Row);
