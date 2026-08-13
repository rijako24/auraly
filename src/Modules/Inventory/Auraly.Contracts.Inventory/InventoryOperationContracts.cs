using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Inventory;

public static class InventoryPermissionCodes
{
    public const string Count = "inventory.counts.confirm";
    public const string Adjust = "inventory.adjustments.confirm";
    public const string Transfer = "inventory.transfers.confirm";
    public const string Convert = "inventory.conversions.confirm";
    public const string Damage = "inventory.damages.confirm";
    public const string Read = "inventory.read";
    public const string ReadCosts = "inventory.costs.read";
    public const string ManageWarehouses = "inventory.warehouses.manage";
    public const string ManageReasons = "inventory.reasons.manage";
}

public static class InventoryDocumentTypes
{
    public const string StockCount = AuralyDocumentTypes.StockCount;
    public const string Adjustment = AuralyDocumentTypes.InventoryAdjustment;
    public const string Transfer = AuralyDocumentTypes.WarehouseTransfer;
    public const string Conversion = AuralyDocumentTypes.ProductConversion;
    public const string Damage = AuralyDocumentTypes.Damage;
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

public sealed record InventoryDamageLineRequest(int LineNumber, Guid ProductId, decimal Quantity);

public sealed record ConfirmInventoryDamageRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WarehouseId,
    DateTimeOffset OccurredAt,
    string ReasonCode,
    Guid? CostCenterId,
    string? Notes,
    IReadOnlyCollection<InventoryDamageLineRequest> Lines);

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

public sealed record InventoryProductQuery(Guid BusinessId, Guid WarehouseId, string? Search, int Page = 1, int PageSize = 50);
public sealed record InventoryProductItem(Guid ProductId, string ProductCode, string? Reference, string ProductName, string UnitCode, decimal QuantityOnHand, decimal? AverageUnitCost, decimal? SaleUnitPrice);
public sealed record InventoryProductPage(IReadOnlyList<InventoryProductItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record InventoryWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record WarehouseMasterItem(Guid WarehouseId, string Code, string Name, bool AllowNegativeStockSales, string PriceFormationCostBasis, bool IsActive);
public sealed record SaveWarehouseRequest(string Name, bool AllowNegativeStockSales, string PriceFormationCostBasis, bool IsActive);
public sealed record InventoryReasonItem(Guid InventoryReasonId, string OperationType, string Code, string Name, bool IsSystem, bool IsActive, int DisplayOrder);
public sealed record SaveInventoryReasonRequest(string OperationType, string Name, bool IsActive, int DisplayOrder);
public sealed record InventoryBalanceQuery(Guid BusinessId, Guid? WarehouseId, string? Search, bool OnlyWithStock, int Page = 1, int PageSize = 50, Guid? ProductId = null);
public sealed record InventoryBalanceItem(Guid WarehouseId, string WarehouseCode, string WarehouseName, Guid ProductId, string ProductCode, string ProductName, decimal QuantityOnHand, decimal? AverageUnitCost, decimal? InventoryValue, DateTimeOffset? UpdatedAt);
public sealed record InventoryBalancePage(IReadOnlyList<InventoryBalanceItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record InventoryMovementQuery(Guid BusinessId, Guid? WarehouseId, Guid? ProductId, string? Search, string? DocumentType, string? MovementType, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 50);
public sealed record InventoryMovementItem(Guid InventoryMovementId, Guid WarehouseId, string WarehouseName, Guid ProductId, string ProductCode, string ProductName, Guid DocumentId, string DocumentType, string? DocumentNumber, string MovementType, decimal QuantityChange, decimal QuantityBefore, decimal QuantityAfter, decimal? RecognizedUnitCost, decimal? ValueChange, DateTimeOffset OccurredAt, DateTimeOffset? PostedAt);
public sealed record InventoryMovementPage(IReadOnlyList<InventoryMovementItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record InventoryOperationQuery(Guid BusinessId, Guid? WarehouseId, string? Search, string? DocumentType, string? Status, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 50);
public sealed record InventoryOperationItem(Guid DocumentId, string DocumentType, string? DocumentNumber, Guid WarehouseId, string WarehouseName, Guid? DestinationWarehouseId, string? DestinationWarehouseName, string ReasonCode, string Status, DateTimeOffset OccurredAt, int LineCount, decimal? TotalValueChange);
public sealed record InventoryOperationPage(IReadOnlyList<InventoryOperationItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public static class InventoryOperationContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(InventoryOperationDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static InventoryOperationDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<InventoryOperationDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The inventory operation payload is invalid.");
}
