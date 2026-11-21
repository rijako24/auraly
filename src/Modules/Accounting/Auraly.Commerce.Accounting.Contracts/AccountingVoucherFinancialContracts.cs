using System.Text.Json.Serialization;

namespace Auraly.Commerce.Accounting.Contracts;

// Capture contains evidence and explicit links, never a second operation selector.
public sealed record VoucherPaymentEvidence(
    string? CardFranchiseCode = null, string? ApprovalNumber = null,
    decimal? TenderedAmount = null, string? Notes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaymentMethodId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodTypeCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PaymentMethodVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodName = null);

// Outcome evidence, not a copy of a form's configuration. The immutable capture
// retains its own account and identity; only this result identifies actual usage.
public sealed record VoucherAppliedPaymentMethod(
    Guid SourceLineId, Guid PaymentMethodId, string MethodCode, int Version,
    string Code, string Name, Guid AccountId, string AccountCode, string AccountName,
    Guid? BankAccountId);

// Server-produced, immutable effects of the versioned manual source. IDs for
// payments/positions are generated once at acceptance and reused on replay.
public sealed record VoucherFinancialEffects(
    Guid AcceptedByUserId,
    IReadOnlyList<VoucherFinancialPaymentGroup> Payments,
    IReadOnlyList<VoucherFinancialPositionEffect> Positions,
    IReadOnlyList<VoucherFinancialCreditApplication> CreditApplications,
    IReadOnlyList<VoucherFinancialMoneyMovement> DirectMovements,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<VoucherFinancialUnapplication>? Unapplications = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<VoucherFinancialConcept>? Concepts = null);

// Minimal document evidence. Amount and account remain on the source line;
// usage flags and the rest of the catalogue configuration are not copied.
public sealed record VoucherFinancialConcept(Guid FinancialConceptId,string Code,string Name);

// The v4 wire name AdvancePositionId is retained for compatibility. For a credit
// application it identifies the existing credit to restore. An actual money
// return has neither an advance nor a credit destination. The original type
// is normalized by the server; it is never another capture/operation selector.
public sealed record VoucherFinancialUnapplication(
    Guid EffectId, Guid GroupId, string SubledgerKind, Guid PositionId,
    Guid ReversesTransactionId, Guid PositionSourceLineId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AdvanceSourceLineId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AdvancePositionId, decimal Amount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReversedTransactionType = null);

public sealed record VoucherFinancialPaymentGroup(
    Guid GroupId, Guid PaymentId, string SubledgerKind,
    Guid PartyId, Guid? CounterpartyRoleId, Guid? PartySiteId,
    IReadOnlyList<VoucherFinancialPaymentAllocation> Allocations,
    IReadOnlyList<VoucherFinancialTender> Tenders,
    Guid? AdvancePositionId = null, decimal AdvanceAmount = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? AdditionalConceptAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? AdditionalSourceLineIds = null);

public sealed record VoucherFinancialPaymentAllocation(
    Guid SourceLineId, Guid SubledgerId, Guid AccountId, decimal Amount,
    bool? FinancialDiscountEarned = null, decimal FinancialDiscountAdjustment = 0m);

public sealed record VoucherFinancialTender(
    Guid SourceLineId, Guid AccountId, Guid? PartyId,
    string MethodCode, Guid? BankAccountId, decimal Amount,
    string? Reference, string? CardFranchiseCode, string? ApprovalNumber,
    decimal? TenderedAmount, string? Notes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaymentMethodId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PaymentMethodVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodName = null);

public sealed record VoucherFinancialPositionEffect(
    Guid EffectId, Guid GroupId, string EffectKind, string SubledgerKind,
    Guid SourceLineId, Guid PositionId, Guid AccountId, Guid PartyId,
    Guid? CounterpartyRoleId, Guid? PartySiteId, decimal Amount, DateTimeOffset? DueDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocumentReference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TargetSourceLineId = null);

public sealed record VoucherFinancialCreditApplication(
    Guid EffectId, Guid GroupId, string SubledgerKind,
    Guid PositionSourceLineId, Guid CreditSourceLineId,
    Guid PositionId, Guid CreditId, decimal Amount);

public sealed record VoucherFinancialMoneyMovement(
    Guid GroupId, Guid SourceLineId, Guid AccountId,
    string MethodCode, Guid? BankAccountId, decimal Amount, string? Reference,
    Guid MovementId = default);
