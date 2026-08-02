using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Inventory;

public static class InventoryPermissionCodes
{
    public const string Count = "inventory.counts.confirm";
    public const string Adjust = "inventory.adjustments.confirm";
    public const string Transfer = "inventory.transfers.confirm";
    public const string Convert = "inventory.conversions.confirm";
}

public static class InventoryDocumentTypes
{
    public const string StockCount = AuralyDocumentTypes.StockCount;
    public const string Adjustment = AuralyDocumentTypes.InventoryAdjustment;
    public const string Transfer = AuralyDocumentTypes.WarehouseTransfer;
    public const string Conversion = AuralyDocumentTypes.ProductConversion;
}

public sealed record InventoryUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record StartStockCountRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WarehouseId,
    DateTimeOffset OccurredAt,
    string ReasonCode,
    string? Notes,
    IReadOnlyCollection<Guid> ProductIds);

public sealed record StockCountLineRequest(int LineNumber, Guid ProductId, decimal CountedQuantity);

public sealed record ConfirmStockCountRequest(
    Guid BusinessId,
    IReadOnlyCollection<StockCountLineRequest> Lines);

public sealed record InventoryAdjustmentLineRequest(
    int LineNumber,
    Guid ProductId,
    decimal QuantityChange,
    decimal? ExplicitUnitCost);

public sealed record ConfirmInventoryAdjustmentRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WarehouseId,
    DateTimeOffset OccurredAt,
    string ReasonCode,
    Guid? CostCenterId,
    string? Notes,
    IReadOnlyCollection<InventoryAdjustmentLineRequest> Lines);

public sealed record WarehouseTransferLineRequest(int LineNumber, Guid ProductId, decimal Quantity);

public sealed record ConfirmWarehouseTransferRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    DateTimeOffset OccurredAt,
    string ReasonCode,
    string? Notes,
    IReadOnlyCollection<WarehouseTransferLineRequest> Lines);

public sealed record ProductConversionLineRequest(
    int LineNumber,
    string Direction,
    Guid ProductId,
    decimal Quantity,
    decimal? AllocationWeight);

public sealed record ConfirmProductConversionRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WarehouseId,
    DateTimeOffset OccurredAt,
    string ConversionType,
    string ReasonCode,
    Guid? CostCenterId,
    string? Notes,
    IReadOnlyCollection<ProductConversionLineRequest> Lines);

public sealed record InventoryOperationLineSnapshot(
    int LineNumber,
    string Direction,
    Guid ProductId,
    string ProductCode,
    string Description,
    decimal Quantity,
    decimal? SystemQuantityAtBase,
    decimal? ExplicitUnitCost,
    decimal? AllocationWeight);

public sealed record InventoryOperationDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid DocumentId,
    string DocumentType,
    Guid WarehouseId,
    Guid? DestinationWarehouseId,
    Guid ConfirmedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    DateTimeOffset OccurredAt,
    string ReasonCode,
    string? ConversionType,
    Guid? CostCenterId,
    long? BaseInventorySequence,
    string? Notes,
    IReadOnlyList<InventoryOperationLineSnapshot> Lines);

public sealed record StockCountDraft(
    Guid DocumentId,
    string Status,
    long BaseInventorySequence,
    IReadOnlyList<InventoryOperationLineSnapshot> Lines);

public sealed record InventoryOperationAcceptance(
    Guid DocumentId,
    Guid MovementId,
    string DocumentType,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

public static class InventoryOperationContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(InventoryOperationDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static InventoryOperationDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<InventoryOperationDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The inventory operation payload is invalid.");
}
