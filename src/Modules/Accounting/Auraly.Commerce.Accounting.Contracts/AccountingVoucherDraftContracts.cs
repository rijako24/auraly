namespace Auraly.Commerce.Accounting.Contracts;

public sealed record VoucherDraftLine(
    Guid? AccountId, Guid? PartyId, Guid? CostCenterId, string Description,
    decimal Debit, decimal Credit, string? Reference = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? LineId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    decimal? BaseAmount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    AccountingOpeningPositionReference? ExistingPosition = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? FinancialGroupId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? DueDate = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? PartySiteId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    VoucherPaymentEvidence? PaymentEvidence = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? FinancialDiscountEarned = null);

public sealed record VoucherDraftAdjustment(
    string SubledgerKind, Guid SubledgerId, string Direction, decimal Amount,
    Guid? CounterpartAccountId, Guid? CostCenterId);

public sealed record SaveVoucherDraftRequest(
    Guid DocumentId, string DocumentType, DateTimeOffset OccurredAt,
    string ConceptCode, string Description, string? Reference,
    IReadOnlyList<VoucherDraftLine> Lines, VoucherDraftAdjustment? Adjustment,
    string? RowVersion,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? MainPartyId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? ClonedFromDocumentId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? PayloadVersion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? VoucherTypeCode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? WorkSessionId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    VoucherFiscalCapture? FiscalCapture = null);

public sealed record CloneVoucherDraftRequest(Guid DocumentId, DateTimeOffset OccurredAt);

// An editable proposal read from the original applied transaction. Acceptance
// revalidates and reserves it; preparing the form has no financial effects.
public sealed record VoucherDraftPreparation(string Description, string? Reference,
    Guid MainPartyId, string MainPartyName, string? MainPartyIdentification,
    IReadOnlyList<VoucherDraftLineView> Lines);

public sealed record SendVoucherDraftRequest(string RowVersion);

public sealed record VoucherDraftLineView(
    VoucherDraftLine Value, string? AccountCode, string? AccountName,
    string? PartyName, string? PartyIdentification, string? CostCenterName, string? ValidationError = null);

public sealed record VoucherDraftView(
    Guid DocumentId, string DocumentType, DateTimeOffset OccurredAt,
    string ConceptCode, string Description, string? Reference, string CurrencyCode,
    IReadOnlyList<VoucherDraftLineView> Lines, VoucherDraftAdjustment? Adjustment,
    string RowVersion, DateTimeOffset? SentAt, string Status,
    bool CanEdit, bool CanSend, AccountingDocumentRow Row,
    Guid? MainPartyId = null, string? MainPartyName = null, string? MainPartyIdentification = null,
    Guid? ClonedFromDocumentId = null, int? PayloadVersion = null,
    // Derived from the accepted source for replay, never a second editable reference.
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? AcceptedReference = null,
    string? VoucherTypeCode = null, Guid? WorkSessionId = null,
    VoucherFinancialEffects? FinancialEffects = null,
    VoucherFiscalCapture? FiscalCapture = null,
    Guid? ReversalDocumentId = null,
    string? ReversalStatus = null);
