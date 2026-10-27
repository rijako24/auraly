using System.Text.Json.Serialization;

namespace Auraly.Commerce.Accounting.Contracts;

// Capture contains evidence and explicit links, never a second operation selector.
public sealed record VoucherPaymentEvidence(
    string? CardFranchiseCode = null, string? ApprovalNumber = null,
    decimal? TenderedAmount = null, string? Notes = null);

// Server-produced, immutable effects of the versioned manual source. IDs for
// payments/positions are generated once at acceptance and reused on replay.
public sealed record VoucherFinancialEffects(
    Guid AcceptedByUserId,
    IReadOnlyList<VoucherFinancialPaymentGroup> Payments,
    IReadOnlyList<VoucherFinancialPositionEffect> Positions,
    IReadOnlyList<VoucherFinancialCreditApplication> CreditApplications,
    IReadOnlyList<VoucherFinancialMoneyMovement> DirectMovements,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<VoucherFinancialUnapplication>? Unapplications = null);

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
    Guid? AdvancePositionId = null, decimal AdvanceAmount = 0m);

public sealed record VoucherFinancialPaymentAllocation(
    Guid SourceLineId, Guid SubledgerId, Guid AccountId, decimal Amount,
    bool? FinancialDiscountEarned = null, decimal FinancialDiscountAdjustment = 0m);

public sealed record VoucherFinancialTender(
    Guid SourceLineId, Guid AccountId, Guid? PartyId,
    string MethodCode, Guid? BankAccountId, decimal Amount,
    string? Reference, string? CardFranchiseCode, string? ApprovalNumber,
    decimal? TenderedAmount, string? Notes);

public sealed record VoucherFinancialPositionEffect(
    Guid EffectId, Guid GroupId, string EffectKind, string SubledgerKind,
    Guid SourceLineId, Guid PositionId, Guid AccountId, Guid PartyId,
    Guid? CounterpartyRoleId, Guid? PartySiteId, decimal Amount, DateTimeOffset? DueDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocumentReference = null);

public sealed record VoucherFinancialCreditApplication(
    Guid EffectId, Guid GroupId, string SubledgerKind,
    Guid PositionSourceLineId, Guid CreditSourceLineId,
    Guid PositionId, Guid CreditId, decimal Amount);

public sealed record VoucherFinancialMoneyMovement(
    Guid GroupId, Guid SourceLineId, Guid AccountId,
    string MethodCode, Guid? BankAccountId, decimal Amount, string? Reference,
    Guid MovementId = default);
