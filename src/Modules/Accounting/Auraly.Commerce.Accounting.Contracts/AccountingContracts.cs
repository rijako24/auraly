namespace Auraly.Commerce.Accounting.Contracts;

public static class AccountingPermissionCodes
{
    public const string Read = "accounting.read";
    public const string Configure = "accounting.configure";
    public const string PeriodsManage = "accounting.periods.manage";
    public const string Retry = "accounting.postings.retry";
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

public sealed record AccountingPostingView(
    Guid SourceDocumentId,
    string SourceDocumentType,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? EntryId);
