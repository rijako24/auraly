using Auraly.Commerce.Taxation.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Purchasing;

public static class PurchasingPermissionCodes
{
    public const string ReadPurchaseOrders = "purchasing.purchase-orders.read";
    public const string CreatePurchaseOrders = "purchasing.purchase-orders.create";
    public const string ConfirmPurchaseOrders = "purchasing.purchase-orders.confirm";
    public const string ClosePurchaseOrders = "purchasing.purchase-orders.close";
    public const string AuthorizeOverReceipt = "purchasing.goods-receipts.over-receive";
    public const string ReadGoodsReceipts = "purchasing.goods-receipts.read";
    public const string CreateGoodsReceipts = "purchasing.goods-receipts.create";
    public const string ConfirmGoodsReceipts = "purchasing.goods-receipts.confirm";
    public const string ReadPurchaseReturns = "purchasing.purchase-returns.read";
    public const string CreatePurchaseReturns = "purchasing.purchase-returns.create";
    public const string ConfirmPurchaseReturns = "purchasing.purchase-returns.confirm";
}

public static class PurchasingDocumentTypes
{
    public const string GoodsReceipt = AuralyDocumentTypes.GoodsReceipt;
    public const string GoodsReceiptCostDocument = "GoodsReceiptCostDocument";
    public const string GoodsReceiptCostRevision = "GoodsReceiptCostRevision";
    public const string PurchaseReturn = AuralyDocumentTypes.PurchaseReturn;
}

public static class PurchasingTaxTreatments
{
    public const string DeductibleInputVat = "DeductibleInputVat";
    public const string CapitalizedCost = "CapitalizedCost";
    public const string NotApplicable = "NotApplicable";
}

public static class PurchaseEvidenceTypes
{
    public const string SupplierElectronicInvoice = "SupplierElectronicInvoice";
    public const string BuyerElectronicSupportDocument = "BuyerElectronicSupportDocument";
    public const string InternalReceiptVoucher = "InternalReceiptVoucher";
    public const string ForeignCommercialInvoice = "ForeignCommercialInvoice";
    public const string ImportDeclaration = "ImportDeclaration";

    public static bool IsValid(string? value) => value is
        SupplierElectronicInvoice or BuyerElectronicSupportDocument or InternalReceiptVoucher or
        ForeignCommercialInvoice or ImportDeclaration;

    public static IReadOnlyList<string> AllowedFor(string? supplierPolicy) => supplierPolicy switch
    {
        SupplierElectronicInvoice => [SupplierElectronicInvoice, InternalReceiptVoucher, ForeignCommercialInvoice],
        BuyerElectronicSupportDocument => [BuyerElectronicSupportDocument, InternalReceiptVoucher, ForeignCommercialInvoice],
        InternalReceiptVoucher => [InternalReceiptVoucher, ForeignCommercialInvoice],
        null => [SupplierElectronicInvoice, BuyerElectronicSupportDocument, InternalReceiptVoucher, ForeignCommercialInvoice],
        _ => []
    };
}

public static class PurchaseCostKinds
{
    public const string Freight = "Freight";
    public const string Insurance = "Insurance";
    public const string CustomsDuty = "CustomsDuty";
    public const string CustomsBrokerage = "CustomsBrokerage";
    public const string Handling = "Handling";
    public const string OtherDirectCost = "OtherDirectCost";
    public const string ImportVat = "ImportVat";

    public static bool IsValid(string? value) => value is Freight or Insurance or CustomsDuty or
        CustomsBrokerage or Handling or OtherDirectCost or ImportVat;
}

public static class PurchaseCostTreatments
{
    public const string Capitalize = "Capitalize";
    public const string Expense = "Expense";
    public static bool IsValid(string? value) => value is Capitalize or Expense;
}

public static class PurchaseCostAllocationMethods
{
    public const string Value = "Value";
    public const string Quantity = "Quantity";
    public const string Weight = "Weight";
    public const string Volume = "Volume";
    public const string Equal = "Equal";
    public const string Manual = "Manual";
    public const string None = "None";
    public static bool IsValid(string? value) => value is Value or Quantity or Weight or Volume or Equal or Manual or None;
}

public sealed record GoodsReceiptCostManualAllocationRequest(int ReceiptLineNumber, decimal FunctionalAmount);

public static class GoodsReceiptCostAssociationModes
{
    public const string Associate = "Associate";
    public const string Distribute = "Distribute";
}

public sealed record GoodsReceiptVoucherCostLineRequest(
    int EntryLineNumber, decimal FunctionalAmount, string AllocationMethod,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GoodsReceiptCostManualAllocationRequest>? ManualAllocations = null);

public sealed record GoodsReceiptVoucherCostRequest(
    Guid AssociationId, Guid EntryId, string Mode,
    IReadOnlyList<GoodsReceiptVoucherCostLineRequest> Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EntryNumber = null);

public sealed record GoodsReceiptVoucherCostLineSnapshot(
    int EntryLineNumber, Guid AccountId, Guid? PartyId, Guid? CostCenterId,
    decimal FunctionalAmount, string AllocationMethod,
    IReadOnlyList<GoodsReceiptVoucherCostAllocationSnapshot> Allocations);

// Source line and method already belong to the containing cost line. Keep only
// allocation facts here instead of repeating those fields for every product.
public sealed record GoodsReceiptVoucherCostAllocationSnapshot(
    int ReceiptLineNumber, decimal Factor, decimal FunctionalAmount);

public sealed record GoodsReceiptVoucherCostSnapshot(
    Guid AssociationId, Guid EntryId, Guid SourceDocumentId, string SourceDocumentType,
    string EntryNumber, string Mode, IReadOnlyList<GoodsReceiptVoucherCostLineSnapshot> Lines,
    int? CostRevisionNumber = null, string? CostRevisionStatus = null);

public sealed record GoodsReceiptCostRevisionPayload(
    Guid TenantId, Guid BusinessId, Guid RevisionId, Guid GoodsReceiptId,
    int RevisionNumber, Guid WarehouseId, Guid SupplierId, Guid AcceptedByUserId,
    string ReceiptNumber, DateTimeOffset OccurredAt,
    IReadOnlyList<GoodsReceiptLineSnapshot> ReceiptLines,
    IReadOnlyList<GoodsReceiptVoucherCostSnapshot> ReferencedCostDocuments,
    GoodsReceiptCostEditSnapshot? CostEdit = null,
    IReadOnlyList<GoodsReceiptCostDocumentSnapshot>? NewAdditionalCostDocuments = null);

public sealed record GoodsReceiptCostEditSnapshot(
    IReadOnlyList<GoodsReceiptLineSnapshot> PreviousLines,
    IReadOnlyList<GoodsReceiptLineSnapshot> CurrentLines,
    decimal PreviousFunctionalGrandTotal,
    decimal CurrentFunctionalGrandTotal,
    WithholdingCalculationSnapshot PreviousWithholding,
    WithholdingCalculationSnapshot CurrentWithholding,
    decimal PreviousPayable,
    decimal CurrentPayable,
    GoodsReceiptFinancialDiscountSnapshot? PreviousFinancialDiscount,
    GoodsReceiptFinancialDiscountSnapshot? CurrentFinancialDiscount,
    IReadOnlyList<GoodsReceiptCostDocumentSnapshot> PreviousAdditionalDocuments,
    IReadOnlyList<GoodsReceiptCostDocumentSnapshot> CurrentAdditionalDocuments,
    IReadOnlyList<GoodsReceiptVoucherCostSnapshot> PreviousReferencedDocuments,
    IReadOnlyList<GoodsReceiptVoucherCostSnapshot> CurrentReferencedDocuments);

public sealed record GoodsReceiptCostVoucherLine(
    int LineNumber, Guid AccountId, string AccountCode, string AccountName,
    Guid? PartyId, string? PartyName, string Description, decimal Debit, decimal Credit,
    decimal EligibleFunctionalAmount, decimal AssignedFunctionalAmount);

public sealed record GoodsReceiptCostVoucher(
    Guid EntryId, string EntryNumber, Guid SourceDocumentId, string SourceDocumentType,
    IReadOnlyList<GoodsReceiptCostVoucherLine> Lines);

public sealed record GoodsReceiptCostVoucherCandidate(
    Guid EntryId, string EntryNumber, string DocumentNumber, DateTimeOffset OccurredAt,
    string PartyName, string Description, string AccountCode, string AccountName,
    string Concept, int EligibleLineCount, decimal EligibleFunctionalAmount,
    decimal AssignedFunctionalAmount);

public sealed record GoodsReceiptCostVoucherCandidatePage(
    IReadOnlyList<GoodsReceiptCostVoucherCandidate> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 :
        (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record GoodsReceiptCostLineRequest(
    int LineNumber, string CostKind, string Description, decimal Amount,
    decimal TaxableBaseAmount, string TaxCode, decimal TaxRate, decimal TaxAmount,
    string TaxTreatment, string CostTreatment, string AllocationMethod,
    IReadOnlyCollection<int>? EligibleReceiptLineNumbers = null,
    IReadOnlyCollection<GoodsReceiptCostManualAllocationRequest>? ManualAllocations = null);

public sealed record GoodsReceiptCostDocumentRequest(
    Guid CostDocumentId, Guid SupplierId, string PurchaseEvidenceType,
    string DocumentNumber, DateTimeOffset IssuedAt, bool CreatesPayable,
    DateTimeOffset? DueDate, string CurrencyCode, decimal ExchangeRate,
    DateOnly? ExchangeRateDate, string ExchangeRateSource,
    IReadOnlyCollection<GoodsReceiptCostLineRequest> Lines,
    string? WithholdingConceptCode = null,
    string? WithholdingJurisdictionCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PartySiteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>? WithholdingAdjustments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WithholdingReviewHash = null);

public sealed record GoodsReceiptLineRequest(
    int LineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
    string TaxTreatment,
    string PresentationName = "Unidad",
    decimal PresentationQuantity = 1,
    decimal UnitsPerPresentation = 1,
    Guid? PurchaseOrderLineId = null,
    string? OverReceiptReason = null,
    decimal? TotalGrossWeightKg = null,
    decimal? TotalVolumeM3 = null,
    decimal? UnitGrossWeightKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? CommercialDiscountPercent = null);

public sealed record GoodsReceiptFinancialDiscountRequest(
    decimal Percent, DateOnly? ExpectedOn, bool AnticipateInCost);

public sealed record GoodsReceiptFinancialDiscountLineSnapshot(
    int ReceiptLineNumber, decimal Amount, decimal FunctionalAmount);

public sealed record GoodsReceiptFinancialDiscountSnapshot(
    decimal Percent, decimal EligibleBase, decimal Amount, decimal FunctionalAmount,
    DateOnly? ExpectedOn, bool AnticipatedInCost,
    IReadOnlyList<GoodsReceiptFinancialDiscountLineSnapshot> Lines);

public sealed record ConfirmGoodsReceiptRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid SupplierId,
    string? SupplierInvoiceNumber,
    DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt,
    bool CreatesPayable,
    DateTimeOffset? DueDate,
    string CurrencyCode,
    string? Notes,
    IReadOnlyCollection<GoodsReceiptLineRequest> Lines,
    string? DraftConcurrencyToken = null,
    string? WithholdingConceptCode = null,
    string? WithholdingJurisdictionCode = null,
    string PurchaseEvidenceType = PurchaseEvidenceTypes.SupplierElectronicInvoice,
    Guid? PurchaseOrderId = null,
    decimal ExchangeRate = 1,
    DateOnly? ExchangeRateDate = null,
    string ExchangeRateSource = "FunctionalCurrency",
    IReadOnlyCollection<GoodsReceiptCostDocumentRequest>? AdditionalCostDocuments = null,
    Guid? PartySiteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>? WithholdingAdjustments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WithholdingReviewHash = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GoodsReceiptVoucherCostRequest>? ReferencedCostDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GoodsReceiptFinancialDiscountRequest? FinancialDiscount = null,
    Guid? CostEditOfReceiptId = null,
    Guid? CostEditDraftId = null,
    int? BaseCostRevisionNumber = null);

public sealed record GoodsReceiptLineSnapshot(
    int LineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
    string TaxTreatment,
    decimal NetAmount,
    decimal TaxAmount,
    decimal LineTotal,
    string PresentationName = "Unidad",
    decimal PresentationQuantity = 1,
    decimal UnitsPerPresentation = 1,
    Guid? PurchaseOrderLineId = null,
    string? OverReceiptReason = null,
    bool OverReceiptAuthorized = false,
    decimal? TotalGrossWeightKg = null,
    decimal? TotalVolumeM3 = null,
    decimal FunctionalNetAmount = 0,
    decimal FunctionalTaxAmount = 0,
    decimal FunctionalLineTotal = 0,
    decimal AllocatedLandedCostAmount = 0,
    decimal RecognizedInventoryCostAmount = 0,
    decimal? UnitGrossWeightKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? CommercialDiscountPercent = null);

public sealed record GoodsReceiptCostAllocationSnapshot(
    int CostLineNumber, int ReceiptLineNumber, decimal Factor,
    decimal FunctionalAmount, string AllocationMethod);

public sealed record GoodsReceiptCostLineSnapshot(
    int LineNumber, string CostKind, string Description, decimal Amount,
    decimal TaxableBaseAmount, string TaxCode, decimal TaxRate, decimal TaxAmount,
    string TaxTreatment, string CostTreatment, string AllocationMethod,
    decimal FunctionalAmount, decimal FunctionalTaxableBaseAmount,
    decimal FunctionalTaxAmount, decimal FunctionalDocumentAmount,
    IReadOnlyList<GoodsReceiptCostAllocationSnapshot> Allocations);

public sealed record GoodsReceiptCostDocumentSnapshot(
    Guid CostDocumentId, Guid SupplierId, string PurchaseEvidenceType,
    string DocumentNumber, DateTimeOffset IssuedAt, bool CreatesPayable,
    DateTimeOffset? DueDate, string CurrencyCode, decimal ExchangeRate,
    DateOnly ExchangeRateDate, string ExchangeRateSource,
    decimal NetAmount, decimal TaxAmount, decimal GrandTotal,
    decimal FunctionalNetAmount, decimal FunctionalTaxAmount, decimal FunctionalGrandTotal,
    WithholdingCalculationSnapshot Withholding,
    IReadOnlyList<GoodsReceiptCostLineSnapshot> Lines, Guid? PartySiteId = null);

public sealed record GoodsReceiptCostDocumentAccountingPayload(
    Guid TenantId, Guid BusinessId, Guid GoodsReceiptId,
    GoodsReceiptCostDocumentSnapshot Document);

public sealed record GoodsReceiptDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid DocumentId,
    Guid WarehouseId,
    Guid SupplierId,
    Guid ConfirmedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    string? SupplierInvoiceNumber,
    DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt,
    bool CreatesPayable,
    DateTimeOffset? DueDate,
    string CurrencyCode,
    string? Notes,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    IReadOnlyList<GoodsReceiptLineSnapshot> Lines,
    WithholdingCalculationSnapshot Withholding,
    string? SupplierNameSnapshot = null,
    string? WarehouseNameSnapshot = null,
    string PurchaseEvidenceType = PurchaseEvidenceTypes.SupplierElectronicInvoice,
    Guid? PurchaseOrderId = null,
    decimal ExchangeRate = 1,
    DateOnly? ExchangeRateDate = null,
    string ExchangeRateSource = "FunctionalCurrency",
    decimal FunctionalNetAmount = 0,
    decimal FunctionalTaxAmount = 0,
    decimal FunctionalGrandTotal = 0,
    IReadOnlyList<GoodsReceiptCostDocumentSnapshot>? AdditionalCostDocuments = null,
    Guid? PartySiteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GoodsReceiptVoucherCostSnapshot>? ReferencedCostDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GoodsReceiptFinancialDiscountSnapshot? FinancialDiscount = null,
    string? WithholdingConceptCode = null);

public sealed record GoodsReceiptAcceptance(
    Guid DocumentId,
    Guid MovementId,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

public sealed record PreviewGoodsReceiptWithholdingRequest(
    Guid BusinessId,
    Guid SupplierId,
    DateTimeOffset SupplierInvoiceDate,
    IReadOnlyCollection<GoodsReceiptLineRequest> Lines,
    string? WithholdingConceptCode = null,
    string? WithholdingJurisdictionCode = null,
    string PurchaseEvidenceType = PurchaseEvidenceTypes.SupplierElectronicInvoice,
    decimal ExchangeRate = 1,
    IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>? WithholdingAdjustments = null);

public sealed record PreviewGoodsReceiptCostWithholdingRequest(
    Guid BusinessId,
    GoodsReceiptCostDocumentRequest Document);

public sealed record SaveGoodsReceiptDraftRequest(
    Guid DraftId,
    Guid BusinessId,
    Guid? WarehouseId,
    Guid? SupplierId,
    string? SupplierInvoiceNumber,
    DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt,
    bool CreatesPayable,
    DateTimeOffset? DueDate,
    string CurrencyCode,
    string? Notes,
    IReadOnlyCollection<GoodsReceiptLineRequest> Lines,
    string? ConcurrencyToken,
    string? PurchaseEvidenceType = null,
    Guid? PurchaseOrderId = null,
    decimal ExchangeRate = 1,
    DateOnly? ExchangeRateDate = null,
    string ExchangeRateSource = "FunctionalCurrency",
    IReadOnlyCollection<GoodsReceiptCostDocumentRequest>? AdditionalCostDocuments = null,
    Guid? PartySiteId = null,
    string? WithholdingConceptCode = null,
    IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>? WithholdingAdjustments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GoodsReceiptVoucherCostRequest>? ReferencedCostDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GoodsReceiptFinancialDiscountRequest? FinancialDiscount = null,
    Guid? CostEditOfReceiptId = null,
    int? BaseCostRevisionNumber = null);

public sealed record GoodsReceiptDraft(
    Guid DraftId, Guid BusinessId, Guid? WarehouseId, Guid? SupplierId,
    string? SupplierInvoiceNumber, DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt, bool CreatesPayable, DateTimeOffset? DueDate,
    string CurrencyCode, string? Notes, decimal NetAmount, decimal TaxAmount,
    decimal GrandTotal, IReadOnlyList<GoodsReceiptLineSnapshot> Lines,
    DateTimeOffset UpdatedAt, string ConcurrencyToken,
    string? PurchaseEvidenceType = null,
    Guid? PurchaseOrderId = null,
    decimal ExchangeRate = 1,
    DateOnly? ExchangeRateDate = null,
    string ExchangeRateSource = "FunctionalCurrency",
    IReadOnlyList<GoodsReceiptCostDocumentRequest>? AdditionalCostDocuments = null,
    Guid? PartySiteId = null,
    string? WithholdingConceptCode = null,
    IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>? WithholdingAdjustments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GoodsReceiptVoucherCostRequest>? ReferencedCostDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GoodsReceiptFinancialDiscountRequest? FinancialDiscount = null,
    Guid? CostEditOfReceiptId = null,
    int? BaseCostRevisionNumber = null);

public sealed record GoodsReceiptCostEditState(
    Guid GoodsReceiptId, int BaseCostRevisionNumber, GoodsReceiptDraft? Draft);

public sealed record GoodsReceiptDetail(
    Guid DocumentId, string DocumentNumber, string Status,
    Guid WarehouseId, string WarehouseName, Guid SupplierId, string SupplierName,
    string? SupplierInvoiceNumber, DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt, bool CreatesPayable, DateTimeOffset? DueDate,
    string CurrencyCode, string? Notes, decimal NetAmount, decimal TaxAmount,
    decimal GrandTotal, DateTimeOffset AcceptedAt, DateTimeOffset? ProcessedAt,
    IReadOnlyList<GoodsReceiptLineSnapshot> Lines,
    string PurchaseEvidenceType = PurchaseEvidenceTypes.SupplierElectronicInvoice,
    Guid? PurchaseOrderId = null,
    WithholdingCalculationSnapshot? Withholding = null,
    decimal ExchangeRate = 1,
    DateOnly? ExchangeRateDate = null,
    string ExchangeRateSource = "FunctionalCurrency",
    decimal FunctionalNetAmount = 0,
    decimal FunctionalTaxAmount = 0,
    decimal FunctionalGrandTotal = 0,
    IReadOnlyList<GoodsReceiptCostDocumentSnapshot>? AdditionalCostDocuments = null,
    IReadOnlyList<GoodsReceiptAccountingStatus>? AccountingStatuses = null,
    IReadOnlyList<GoodsReceiptVoucherCostSnapshot>? ReferencedCostDocuments = null,
    GoodsReceiptPayableView? Payable = null,
    GoodsReceiptFinancialDiscountSnapshot? FinancialDiscount = null,
    Guid? PartySiteId = null,
    string? WithholdingConceptCode = null);

public sealed record GoodsReceiptPayableView(
    Guid PayableId, string Status, decimal OutstandingAmount, string CurrencyCode,
    Guid? PartySiteId, string? PartySiteName, DateTimeOffset DueDate);

public sealed record GoodsReceiptAccountingStatus(
    Guid SourceDocumentId, string SourceDocumentType, string Status,
    string? ErrorCode, string? ErrorMessage);

public sealed record GoodsReceiptListItem(
    Guid DocumentId, string? DocumentNumber, string Status,
    Guid? WarehouseId, string? WarehouseName, Guid? SupplierId, string? SupplierName,
    string? SupplierInvoiceNumber, DateTimeOffset ReceivedAt,
    decimal GrandTotal, DateTimeOffset UpdatedAt,
    string? PurchaseEvidenceType = null);

public sealed record GoodsReceiptPage(
    IReadOnlyList<GoodsReceiptListItem> Items, int Page, int PageSize,
    int TotalCount, int TotalPages);

public sealed record GoodsReceiptWorkspaceOptions(
    IReadOnlyList<GoodsReceiptWarehouseOption> Warehouses,
    IReadOnlyList<GoodsReceiptSupplierOption> Suppliers,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseEvidenceTypes,
    IReadOnlyList<GoodsReceiptTaxOption> WithholdingConcepts,
    IReadOnlyList<GoodsReceiptTaxOption> WithholdingJurisdictions,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseCostEvidenceTypes,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseCostKinds,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseCostTreatments,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseCostAllocationMethods,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseTaxRates,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseTaxTreatments,
    IReadOnlyList<PurchaseEvidenceTypeOption> PurchaseCurrencies,
    IReadOnlyList<PurchaseEvidenceTypeOption> ExchangeRateSources,
    IReadOnlyList<PurchaseEvidenceTypeOption>? PurchaseCostAssociationModes = null);

public sealed record GoodsReceiptWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record GoodsReceiptSupplierOption(
    Guid SupplierId, string Identification, string Name,
    string? PurchaseEvidencePolicy,
    IReadOnlyList<string> AllowedPurchaseEvidenceTypes);
public sealed record PurchaseEvidenceTypeOption(string Code, string Label, string? Description);
public sealed record GoodsReceiptTaxOption(string Code, string Label);

public sealed record GoodsReceiptProductOption(
    Guid ProductId, string ProductCode, string? Reference, string Name,
    string? SupplierProductCode, decimal? LatestUnitCost, decimal? AverageUnitCost,
    string TaxCode, decimal TaxRate, string TaxTreatment, IReadOnlyList<string> Barcodes, string BaseUnitCode,
    bool IsAssociated, string PurchasePresentationName = "Unidad",
    decimal UnitsPerPresentation = 1, bool IsPrimary = false,
    decimal? UnitGrossWeightKg = null);

public sealed record AssociateGoodsReceiptProductRequest(
    Guid SupplierId, Guid ProductId, string? SupplierProductCode, bool IsPrimary,
    string PurchasePresentationName = "Unidad", decimal UnitsPerPresentation = 1);

public sealed record GoodsReceiptProductPage(
    IReadOnlyList<GoodsReceiptProductOption> Items, int Page, int PageSize,
    int TotalCount, int TotalPages);

public sealed record PurchasingUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public static class GoodsReceiptContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(GoodsReceiptDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static GoodsReceiptDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<GoodsReceiptDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The goods receipt payload is invalid.");

    public static string SerializeCostDocument(GoodsReceiptCostDocumentAccountingPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static GoodsReceiptCostDocumentAccountingPayload DeserializeCostDocument(string payload) =>
        JsonSerializer.Deserialize<GoodsReceiptCostDocumentAccountingPayload>(payload, Options)
        ?? throw new InvalidOperationException("The goods receipt cost document payload is invalid.");

    public static string SerializeCostRevision(GoodsReceiptCostRevisionPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static GoodsReceiptCostRevisionPayload DeserializeCostRevision(string payload) =>
        JsonSerializer.Deserialize<GoodsReceiptCostRevisionPayload>(payload, Options)
        ?? throw new InvalidOperationException("The goods receipt cost revision payload is invalid.");
}

public sealed record PurchaseReturnLineRequest(int OriginalLineNumber, decimal Quantity);

public sealed record ConfirmPurchaseReturnRequest(
    Guid ReturnId, Guid BusinessId, Guid OriginalGoodsReceiptId,
    DateTimeOffset ReturnedAt, string ReasonCode, string? Notes,
    IReadOnlyCollection<PurchaseReturnLineRequest> Lines);

public sealed record PurchaseReturnLineSnapshot(
    int LineNumber, int OriginalLineNumber, Guid ProductId, string Description,
    decimal Quantity, decimal UnitCost, decimal DiscountAmount, string TaxCode,
    decimal TaxRate, string TaxTreatment, decimal NetAmount, decimal TaxAmount,
    decimal LineTotal, decimal RecognizedUnitCost,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    decimal FinancialDiscountAmount = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    decimal FinancialDiscountAdjustment = 0m);

public sealed record PurchaseReturnDocumentPayload(
    Guid TenantId, Guid BusinessId, Guid ReturnId, Guid OriginalGoodsReceiptId,
    Guid WarehouseId, Guid SupplierId, Guid ConfirmedByUserId,
    string DocumentNumber, Guid DocumentSeriesId, string DocumentPrefix,
    string DocumentSeriesCode, long DocumentConsecutive, DateTimeOffset ReturnedAt,
    string ReasonCode, string? Notes, string CurrencyCode, decimal NetAmount,
    decimal TaxAmount, decimal TotalAmount, IReadOnlyList<PurchaseReturnLineSnapshot> Lines,
    string? SupplierNameSnapshot = null,string? WarehouseNameSnapshot = null);

public sealed record PurchaseReturnAcceptance(
    Guid ReturnId, Guid MovementId, string DocumentNumber, string Status,
    long ProcessingSequence, bool IdempotentReplay);

public sealed record ReturnableGoodsReceiptLine(
    int OriginalLineNumber, Guid ProductId, string Description,
    decimal ReceivedQuantity, decimal ReturnedQuantity, decimal AvailableQuantity,
    decimal UnitCost, decimal NetAmount, decimal TaxAmount, decimal LineTotal);

public sealed record ReturnableGoodsReceipt(
    Guid GoodsReceiptId, string DocumentNumber, Guid WarehouseId, string WarehouseName,
    Guid SupplierId, string SupplierName, string? SupplierInvoiceNumber,
    DateTimeOffset ReceivedAt, string CurrencyCode, decimal GrandTotal,
    IReadOnlyList<ReturnableGoodsReceiptLine> Lines);

public sealed record ReturnableGoodsReceiptListItem(
    Guid GoodsReceiptId, string DocumentNumber, string SupplierName, string WarehouseName,
    string? SupplierInvoiceNumber, DateTimeOffset ReceivedAt, decimal GrandTotal,
    decimal ReturnedTotal, bool HasAvailableQuantity);

public sealed record ReturnableGoodsReceiptPage(
    IReadOnlyList<ReturnableGoodsReceiptListItem> Items, int Page, int PageSize,
    int TotalCount, int TotalPages);

public static class PurchaseReturnContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(PurchaseReturnDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static PurchaseReturnDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<PurchaseReturnDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The purchase return payload is invalid.");
}
