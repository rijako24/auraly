namespace Auraly.Commerce.Accounting.Contracts;

public sealed record AccountingOpenPositionQuery(
    string SubledgerKind, int Page, int PageSize, Guid? PartyId = null,
    Guid? AccountId = null, string? Search = null, DateOnly? DueFrom = null,
    DateOnly? DueTo = null, Guid? SubledgerId = null, Guid? CounterpartyRoleId = null, bool ManualSupportOnly = false,
    DateOnly? EffectiveOn = null);

public sealed record AccountingOpenPosition(
    string SubledgerKind, Guid SubledgerId, Guid PartyId, string PartyName,
    string? PartyIdentification, Guid SourceDocumentId, string SourceDocumentType,
    string DocumentNumber, DateTimeOffset OccurredAt, DateTimeOffset? DueDate,
    string? CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    decimal PendingPaymentAmount, decimal PendingAdjustmentAmount, decimal AvailableAmount,
    Guid? OpeningEntryId, int? OpeningEntryLineNumber, Guid? AccountId,
    string? AccountCode, string? AccountName, bool? AccountIsActive,
    decimal? OpeningDebit, decimal? OpeningCredit, string? OpeningLinkIssue,
    Guid? CounterpartyRoleId, string Status,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    decimal PendingApplicationAmount = 0m,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? CreditNature = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? PartySiteId = null,
    decimal? FinancialDiscountEligibleAmount = null, bool? FinancialDiscountAnticipated = null,
    bool FinancialDiscountDecided = false, bool FinancialDiscountPendingReturn = false,
    Guid? FinancialDiscountEarnedAccountId = null, string? FinancialDiscountEarnedAccountCode = null,
    string? FinancialDiscountEarnedAccountName = null,
    Guid? FinancialDiscountLostAccountId = null, string? FinancialDiscountLostAccountCode = null,
    string? FinancialDiscountLostAccountName = null);

public sealed record AccountingOpenPositionPage(
    IReadOnlyList<AccountingOpenPosition> Items, int Page, int PageSize,
    int TotalCount, decimal TotalOutstandingAmount);
