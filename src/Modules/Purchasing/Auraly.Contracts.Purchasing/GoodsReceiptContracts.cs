using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Purchasing;

public static class PurchasingPermissionCodes
{
    public const string ReadGoodsReceipts = "purchasing.goods-receipts.read";
    public const string CreateGoodsReceipts = "purchasing.goods-receipts.create";
    public const string ConfirmGoodsReceipts = "purchasing.goods-receipts.confirm";
}

public static class PurchasingDocumentTypes
{
    public const string GoodsReceipt = AuralyDocumentTypes.GoodsReceipt;
}

public static class PurchasingTaxTreatments
{
    public const string DeductibleInputVat = "DeductibleInputVat";
    public const string CapitalizedCost = "CapitalizedCost";
    public const string NotApplicable = "NotApplicable";
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
    string TaxTreatment);

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
    string? DraftConcurrencyToken = null);

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
    decimal LineTotal);

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
    IReadOnlyList<GoodsReceiptLineSnapshot> Lines);

public sealed record GoodsReceiptAcceptance(
    Guid DocumentId,
    Guid MovementId,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

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
    string? ConcurrencyToken);

public sealed record GoodsReceiptDraft(
    Guid DraftId, Guid BusinessId, Guid? WarehouseId, Guid? SupplierId,
    string? SupplierInvoiceNumber, DateTimeOffset? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt, bool CreatesPayable, DateTimeOffset? DueDate,
    string CurrencyCode, string? Notes, decimal NetAmount, decimal TaxAmount,
    decimal GrandTotal, IReadOnlyList<GoodsReceiptLineSnapshot> Lines,
    DateTimeOffset UpdatedAt, string ConcurrencyToken);

public sealed record GoodsReceiptListItem(
    Guid DocumentId, string? DocumentNumber, string Status,
    Guid? WarehouseId, string? WarehouseName, Guid? SupplierId, string? SupplierName,
    string? SupplierInvoiceNumber, DateTimeOffset ReceivedAt,
    decimal GrandTotal, DateTimeOffset UpdatedAt);

public sealed record GoodsReceiptPage(
    IReadOnlyList<GoodsReceiptListItem> Items, int Page, int PageSize,
    int TotalCount, int TotalPages);

public sealed record GoodsReceiptWorkspaceOptions(
    IReadOnlyList<GoodsReceiptWarehouseOption> Warehouses,
    IReadOnlyList<GoodsReceiptSupplierOption> Suppliers);

public sealed record GoodsReceiptWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record GoodsReceiptSupplierOption(Guid SupplierId, string Identification, string Name);

public sealed record GoodsReceiptProductOption(
    Guid ProductId, string ProductCode, string? Reference, string Name,
    string? SupplierProductCode, decimal? LatestUnitCost,
    string TaxCode, decimal TaxRate, IReadOnlyList<string> Barcodes);

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
