using Auraly.Commerce.Taxation.Contracts;

using System.Text.Json;
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

    public static bool IsValid(string? value) => value is
        SupplierElectronicInvoice or BuyerElectronicSupportDocument or InternalReceiptVoucher;

    public static IReadOnlyList<string> AllowedFor(string? supplierPolicy) => supplierPolicy switch
    {
        SupplierElectronicInvoice => [SupplierElectronicInvoice, InternalReceiptVoucher],
        BuyerElectronicSupportDocument => [BuyerElectronicSupportDocument, InternalReceiptVoucher],
        InternalReceiptVoucher => [InternalReceiptVoucher],
        null => [SupplierElectronicInvoice, BuyerElectronicSupportDocument, InternalReceiptVoucher],
        _ => []
    };
}

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
    string? OverReceiptReason = null);

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
    Guid? PurchaseOrderId = null);

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
    bool OverReceiptAuthorized = false);

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
    Guid? PurchaseOrderId = null);

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
    string PurchaseEvidenceType = PurchaseEvidenceTypes.SupplierElectronicInvoice);

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
    Guid? PurchaseOrderId = null);

public sealed record GoodsReceiptDraft(
    Guid DraftId, Guid BusinessId, Guid? WarehouseId, Guid? SupplierId,
    string? SupplierInvoiceNumber, DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt, bool CreatesPayable, DateTimeOffset? DueDate,
    string CurrencyCode, string? Notes, decimal NetAmount, decimal TaxAmount,
    decimal GrandTotal, IReadOnlyList<GoodsReceiptLineSnapshot> Lines,
    DateTimeOffset UpdatedAt, string ConcurrencyToken,
    string? PurchaseEvidenceType = null,
    Guid? PurchaseOrderId = null);

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
    WithholdingCalculationSnapshot? Withholding = null);

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
    IReadOnlyList<GoodsReceiptTaxOption> WithholdingJurisdictions);

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
    decimal UnitsPerPresentation = 1, bool IsPrimary = false);

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
    decimal LineTotal, decimal RecognizedUnitCost);

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
