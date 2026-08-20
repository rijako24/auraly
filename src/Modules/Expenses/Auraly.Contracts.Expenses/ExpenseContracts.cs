using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Contracts.Expenses;

public static class ExpensePermissionCodes
{
    public const string Read = "expenses.read";
    public const string Create = "expenses.create";
    public const string Configure = "expenses.configure";
}

public static class ExpenseDocumentTypes
{
    public const string Expense = AuralyDocumentTypes.Expense;
}

public sealed record ExpenseUserIdentity(Guid UserId, Guid TenantId, Guid BusinessId, IReadOnlySet<string> Permissions);

public sealed record SaveExpenseConceptRequest(Guid ConceptId, Guid BusinessId, string Code, string Name,
    Guid ExpenseAccountId, Guid? DefaultCostCenterId, string? WithholdingConceptCode, bool IsActive);

public sealed record ExpenseConceptView(Guid ConceptId, Guid BusinessId, string Code, string Name,
    Guid ExpenseAccountId, string ExpenseAccountCode, string ExpenseAccountName,
    Guid? DefaultCostCenterId, string? DefaultCostCenterName, string? WithholdingConceptCode, bool IsActive);

public sealed record ExpenseWorkspaceOptions(IReadOnlyList<ExpenseConceptView> Concepts,
    IReadOnlyList<ExpenseSupplierOption> Suppliers, IReadOnlyList<ExpenseAccountOption> ExpenseAccounts,
    IReadOnlyList<ExpenseCostCenterOption> CostCenters);
public sealed record ExpenseSupplierOption(Guid SupplierId, string Identification, string Name);
public sealed record ExpenseAccountOption(Guid AccountId, string Code, string Name);
public sealed record ExpenseCostCenterOption(Guid CostCenterId, string Code, string Name, bool IsDefault);

public sealed record ConfirmExpenseRequest(Guid ExpenseId, Guid BusinessId, Guid SupplierId, Guid ConceptId,
    Guid? CostCenterId, string SupplierDocumentNumber, DateTimeOffset IssuedAt, DateTimeOffset DueDate,
    string CurrencyCode, string Description, decimal TaxExclusiveAmount, decimal VatAmount,
    string? WithholdingJurisdictionCode, string? EvidenceUrl);

public sealed record ExpenseDocumentPayload(Guid TenantId, Guid BusinessId, Guid ExpenseId, Guid SupplierId,
    Guid ConceptId, Guid ExpenseAccountId, Guid? CostCenterId, Guid ConfirmedByUserId, string DocumentNumber,
    Guid DocumentSeriesId, string DocumentPrefix, string DocumentSeriesCode, long DocumentConsecutive,
    string SupplierDocumentNumber, DateTimeOffset IssuedAt, DateTimeOffset DueDate, string CurrencyCode,
    string Description, decimal TaxExclusiveAmount, decimal VatAmount, decimal GrossAmount,
    string? EvidenceUrl, WithholdingCalculationSnapshot Withholding);

public sealed record ExpenseAcceptance(Guid ExpenseId, Guid MovementId, string DocumentNumber,
    string Status, long ProcessingSequence, bool IdempotentReplay);

public sealed record ExpenseListItem(Guid ExpenseId, string DocumentNumber, string SupplierDocumentNumber,
    Guid SupplierId, string SupplierName, Guid ConceptId, string ConceptName, DateTimeOffset IssuedAt,
    DateTimeOffset DueDate, decimal GrossAmount, decimal WithholdingAmount, decimal NetPayable,
    string CurrencyCode, string Status, string? EvidenceUrl);
public sealed record ExpensePage(IReadOnlyList<ExpenseListItem> Items, int Page, int PageSize, int TotalCount,
    decimal GrossTotal, decimal WithholdingTotal, decimal NetPayableTotal)
{ public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize); }

public static class ExpenseContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(ExpenseDocumentPayload payload) => JsonSerializer.Serialize(payload, Options);
    public static ExpenseDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<ExpenseDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The expense payload is invalid.");
}
