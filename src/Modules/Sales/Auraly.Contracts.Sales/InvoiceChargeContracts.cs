using Auraly.Contracts.Expenses;
using Auraly.Contracts.Catalog;

namespace Auraly.Contracts.Sales;

public static class InvoiceChargePermissions
{
    public const string Read = "invoice-charges.read";
    public const string Configure = "invoice-charges.configure";
}

public sealed record InvoiceChargeActor(Guid TenantId, Guid BusinessId, Guid UserId,
    IReadOnlySet<string> Permissions);

public sealed record InvoiceChargeTariff(decimal FromInclusive, decimal? ToExclusive,
    string CalculationMode, decimal Value);

public sealed record SaveInvoiceChargeRequest(Guid ChargeId, long ExpectedVersion,
    string Code, string Name, bool IsActive, int SortOrder, string CalculationMode,
    decimal? Value, string InclusionMode, decimal? InvoiceAmountLimit,
    Guid ExpenseConceptId, Guid SalesTaxProfileId,
    IReadOnlyList<InvoiceChargeTariff> Ranges, IReadOnlyList<Guid> SupplierIds,
    Guid PurchaseTaxProfileId);

public sealed record InvoiceChargeDefinition(Guid ChargeId, Guid BusinessId, long Version,
    string Code, string Name, bool IsActive, int SortOrder, string CalculationMode,
    decimal? Value, string InclusionMode, decimal? InvoiceAmountLimit,
    Guid ExpenseConceptId, string ExpenseConceptName, Guid ExpenseAccountId,
    string ExpenseAccountCode, string ExpenseAccountName,
    Guid? CostCenterId, string? CostCenterName,
    Guid SalesTaxProfileId, string SalesTaxProfileName, string TaxCode, decimal TaxRate,
    IReadOnlyList<InvoiceChargeTariff> Ranges, IReadOnlyList<InvoiceChargeSupplier> Suppliers,
    Guid PurchaseTaxProfileId = default, string PurchaseTaxProfileName = "", decimal PurchaseTaxRate = 0,
    string? WithholdingConceptCode = null);

public sealed record InvoiceChargeSupplier(Guid SupplierId, string Name, string? Identification,
    int DefaultPaymentDueDays, bool IsActive, bool AppliesWithholding = false,
    IReadOnlyList<string>? TaxResponsibilities = null, string? TaxJurisdictionCode = null,
    string? PurchaseEvidencePolicy = null);

public sealed record InvoiceChargeSelection(Guid AppliedChargeId, InvoiceChargeDefinition Definition,
    Guid SupplierId, decimal? ManualAmount);

public sealed record InvoiceChargeDraftRequest(Guid AppliedChargeId, Guid ChargeId, long ChargeVersion,
    Guid SupplierId, decimal? ManualAmount, long ExpectedVersion);

public sealed record AppliedInvoiceCharge(Guid AppliedChargeId, Guid ChargeId, long Version,
    string Code, string Name, decimal InvoiceBase, decimal Amount, decimal InvoicedAmount,
    decimal ExpenseAmount, decimal InvoicedUntaxedAmount, decimal InvoicedTaxAmount,
    string TaxCode, decimal TaxRate, decimal SupplierUntaxedAmount, decimal SupplierVatAmount,
    Guid ExpenseConceptId, Guid ExpenseAccountId, Guid? CostCenterId,
    string? WithholdingConceptCode, InvoiceChargeSupplier Supplier, decimal? ManualAmount = null);

public sealed record InvoiceChargePage(IReadOnlyList<InvoiceChargeDefinition> Items,
    int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record InvoiceChargeConfigurationOptions(IReadOnlyList<ExpenseConceptView> Concepts,
    IReadOnlyList<TaxProfileSummary> TaxProfiles);

public sealed record InvoiceChargeHistoryItem(Guid DocumentId, string DocumentNumber, DateTimeOffset IssuedAt,
    Guid WorkSessionId, AppliedInvoiceCharge Charge, string? ExpenseDocumentNumber,
    string? ExpenseStatus, decimal? PayableBalance);
public sealed record InvoiceChargeHistoryPage(IReadOnlyList<InvoiceChargeHistoryItem> Items, int Page,
    int PageSize, int TotalCount, decimal InvoicedTotal, decimal ExpenseTotal);
