namespace Auraly.Commerce.Accounting.Contracts;

public static class AccountingPermissionCodes
{
    public const string Read = "accounting.read";
    public const string Configure = "accounting.configure";
    public const string PeriodsManage = "accounting.periods.manage";
    public const string Retry = "accounting.postings.retry";
    public const string Activate = "accounting.activate";
    public const string ManualCreate = "accounting.manual.create";
}

public static class AccountingCategories
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string DebitCardClearing = "DebitCardClearing";
    public const string CreditCardClearing = "CreditCardClearing";
    public const string TransferClearing = "TransferClearing";
    public const string AccountsReceivable = "AccountsReceivable";
    public const string SalesRevenue = "SalesRevenue";
    public const string SalesReturns = "SalesReturns";
    public const string OutputVat = "OutputVat";
    public const string Inventory = "Inventory";
    public const string CostOfGoodsSold = "CostOfGoodsSold";
    public const string CustomerCreditsPayable = "CustomerCreditsPayable";
    public const string AccountsPayable = "AccountsPayable";
    public const string SupplierCreditsReceivable = "SupplierCreditsReceivable";
    public const string InputVat = "InputVat";
    public const string PurchasesExpense = "PurchasesExpense";
    public const string WithholdingIncomeTaxPayable = "WithholdingIncomeTaxPayable";
    public const string WithholdingVatPayable = "WithholdingVatPayable";
    public const string WithholdingIcaPayable = "WithholdingIcaPayable";
    public const string WithholdingIncomeTaxReceivable = "WithholdingIncomeTaxReceivable";
    public const string WithholdingVatReceivable = "WithholdingVatReceivable";
    public const string WithholdingIcaReceivable = "WithholdingIcaReceivable";
    public const string OtherIncome = "OtherIncome";
    public const string OwnerContributions = "OwnerContributions";
    public const string OperatingExpense = "OperatingExpense";
    public const string OtherExpense = "OtherExpense";
    public const string CashOverageIncome = "CashOverageIncome";
    public const string CashShortageExpense = "CashShortageExpense";
    public const string DispatchCashOverageIncome = "DispatchCashOverageIncome";
    public const string DispatchCashShortageExpense = "DispatchCashShortageExpense";
    public const string InventoryDifferences = "InventoryDifferences";
    public const string DamagedInventoryExpense = "DamagedInventoryExpense";
    public const string ConversionLossExpense = "ConversionLossExpense";
    public const string TransferLossExpense = "TransferLossExpense";

}

public static class AccountingPostingStatuses
{
    public const string Pending = "Pending";
    public const string PendingConfiguration = "AccountingPendingConfiguration";
    public const string Posted = "Posted";
}

public sealed record AccountingUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record CreateAccountingAccountRequest(
    Guid AccountId,
    Guid TenantId,
    string Code,
    string Name,
    string AccountType,
    bool AllowsPosting,
    bool RequiresParty);

public sealed record AccountingAccountView(
    Guid AccountId,
    string Code,
    string Name,
    string AccountType,
    bool AllowsPosting,
    bool RequiresParty,
    bool IsActive);

public sealed record CreateCostCenterRequest(
    Guid CostCenterId,
    Guid BusinessId,
    string Code,
    string Name,
    Guid? ParentCostCenterId,
    bool IsDefault);

public sealed record AccountingCostCenterView(
    Guid CostCenterId,
    Guid BusinessId,
    string Code,
    string Name,
    Guid? ParentCostCenterId,
    bool IsDefault,
    bool IsActive);

public sealed record CreateAccountingPeriodRequest(
    Guid PeriodId,
    Guid TenantId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Name);

public sealed record AccountingPeriodView(
    Guid PeriodId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Name,
    string Status);

public sealed record SetAccountMappingRequest(
    Guid TenantId,
    Guid? BusinessId,
    string Category,
    Guid AccountId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
public sealed record AccountingMappingView(
    Guid MappingId,
    Guid TenantId,
    Guid? BusinessId,
    string Category,
    Guid AccountId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record AccountingDefaultsResult(
    int AccountCount,
    int MappingCount,
    bool HasDefaultCostCenter,
    bool HasOpenPeriod,
    bool IsReady);

public static class AccountingActivationStatuses
{
    public const string Disabled = "Disabled";
    public const string Configuring = "Configuring";
    public const string Ready = "Ready";
}

public sealed record ActivateAccountingRequest(
    DateOnly EffectiveFrom,
    string FunctionalCurrencyCode,
    string OpeningBalanceMode);

public sealed record AccountingReadinessView(
    string Status,
    string FunctionalCurrencyCode,
    DateOnly? EffectiveFrom,
    string? OpeningBalanceMode,
    DateTimeOffset? ActivatedAt,
    IReadOnlyList<string> BlockingIssues);

public static class AccountingOpeningBalanceStatuses
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string Posted = "Posted";
}

public sealed record AccountingOpeningBalanceLineRequest(
    Guid AccountId, Guid? PartyId, Guid? CostCenterId, string Description,
    decimal Debit, decimal Credit);

public sealed record SaveAccountingOpeningBalanceRequest(
    Guid BatchId, Guid BusinessId, DateOnly EffectiveOn, string CurrencyCode,
    string Description, string? RowVersion,
    IReadOnlyList<AccountingOpeningBalanceLineRequest> Lines);

public sealed record AccountingOpeningBalanceLineView(
    int LineNumber, Guid AccountId, Guid? PartyId, Guid? CostCenterId,
    string Description, decimal Debit, decimal Credit);

public sealed record AccountingOpeningBalanceView(
    Guid BatchId, Guid BusinessId, DateOnly EffectiveOn, string CurrencyCode,
    string Description, string Status, decimal DebitTotal, decimal CreditTotal,
    string RowVersion, DateTimeOffset UpdatedAt, DateTimeOffset? ApprovedAt,
    DateTimeOffset? PostedAt, IReadOnlyList<AccountingOpeningBalanceLineView> Lines);

public sealed record AccountingOpeningBalancePosting(
    Guid BatchId, Guid BusinessId);

public static class AccountingManualDocumentTypes
{
    public const string AccountAdjustment = "AccountAdjustment";
    public const string ManualVoucher = "ManualAccountingVoucher";
    public const string OpeningBalance = "AccountingOpeningBalance";
}

public static class AccountingSubledgerKinds
{
    public const string Receivable = "Receivable";
    public const string Payable = "Payable";
}

public static class AccountingAdjustmentDirections
{
    public const string Increase = "Increase";
    public const string Decrease = "Decrease";
}

public sealed record ConfirmAccountAdjustmentRequest(
    Guid AdjustmentId, Guid BusinessId, string SubledgerKind, Guid SubledgerId,
    string Direction, decimal Amount, Guid CounterpartAccountId,
    Guid? CostCenterId, DateTimeOffset OccurredAt, string ConceptCode,
    string Description);

public sealed record ManualVoucherLineRequest(
    Guid AccountId, Guid? PartyId, Guid? CostCenterId, string Description,
    decimal Debit, decimal Credit);

public sealed record ConfirmManualAccountingVoucherRequest(
    Guid VoucherId, Guid BusinessId, DateTimeOffset OccurredAt,
    string ConceptCode, string Description,
    IReadOnlyList<ManualVoucherLineRequest> Lines);

public sealed record AccountingManualDocumentAcceptance(
    Guid DocumentId, string DocumentType, string Status, bool IsDuplicate);

public sealed record AccountingCategoryDefinition(
    string Category,
    string DisplayName,
    string AccountType,
    bool IsRequired,
    int DisplayOrder);

public sealed record AccountingEntryLineView(
    int LineNumber,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    Guid? PartyId,
    Guid? CostCenterId,
    string Description);

public sealed record AccountingEntryView(
    Guid EntryId,
    string EntryNumber,
    Guid SourceDocumentId,
    string SourceDocumentType,
    DateTimeOffset OccurredAt,
    DateTimeOffset PostedAt,
    decimal DebitTotal,
    decimal CreditTotal,
    IReadOnlyList<AccountingEntryLineView> Lines);

public sealed record TrialBalanceRow(
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record AccountMovementRow(
    Guid EntryId,
    string EntryNumber,
    Guid SourceDocumentId,
    string SourceDocumentType,
    DateTimeOffset OccurredAt,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record AccountingJournalRow(
    Guid EntryId, string EntryNumber, DateTimeOffset OccurredAt,
    Guid SourceDocumentId, string SourceDocumentType, int LineNumber,
    string AccountCode, string AccountName, Guid? PartyId, Guid? CostCenterId,
    string Description, decimal Debit, decimal Credit);

public sealed record GeneralLedgerRow(
    string AccountCode, string AccountName, string AccountType,
    decimal OpeningBalance, decimal Debit, decimal Credit, decimal ClosingBalance);

public sealed record FinancialStatementRow(
    string Section, string AccountCode, string AccountName, decimal Amount);

public sealed record AccountingExceptionRow(
    Guid SourceDocumentId, string SourceDocumentType, DateTimeOffset OccurredAt,
    string Status, string? ErrorCode, string? ErrorMessage);

public sealed record AccountingPostingView(
    Guid SourceDocumentId,
    string SourceDocumentType,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? EntryId);
