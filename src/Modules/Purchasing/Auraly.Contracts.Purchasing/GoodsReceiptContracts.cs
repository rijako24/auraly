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

public sealed record GoodsReceiptLineRequest(
    int LineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate);

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
    IReadOnlyCollection<GoodsReceiptLineRequest> Lines);

public sealed record GoodsReceiptLineSnapshot(
    int LineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
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
