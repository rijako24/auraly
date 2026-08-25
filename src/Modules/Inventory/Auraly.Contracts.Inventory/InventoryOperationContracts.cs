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
    public const string ManagePhysicalCounts = "inventory.physical-counts.manage";
    public const string CapturePhysicalCounts = "inventory.physical-counts.capture";
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
    IReadOnlyCollection<StartStockCountLineRequest> Lines);

public sealed record StartStockCountLineRequest(Guid ProductId, decimal PreCountQuantity);

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
    decimal? PreCountQuantity,
    decimal? SystemQuantityAtBase,
    decimal? ExplicitUnitCost,
    decimal? AllocationWeight,
    decimal? ConversionFactor = null,
    decimal? ConversionEquivalentQuantity = null);

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
    IReadOnlyList<InventoryOperationLineSnapshot> Lines,
    Guid? ConversionFamilyRootProductId = null,
    decimal? ConversionInputEquivalent = null,
    decimal? ConversionOutputEquivalent = null,
    decimal? ConversionLossQuantity = null,
    decimal? ConversionLossPercent = null,
    decimal? ConversionMaximumLossPercent = null);

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

public sealed record InventoryProductQuery(Guid BusinessId, Guid WarehouseId, string? Search, int Page = 1, int PageSize = 50, Guid? ProductCategoryId = null);
public sealed record InventoryProductItem(Guid ProductId, string ProductCode, string? Reference, string ProductName, string UnitCode, decimal QuantityOnHand, decimal? AverageUnitCost, decimal? SaleUnitPrice, Guid? ProductCategoryId = null, string? ProductCategoryName = null);
public sealed record InventoryProductPage(IReadOnlyList<InventoryProductItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record ProductConversionProductQuery(Guid BusinessId, Guid WarehouseId, Guid? FamilyRootProductId, string? Search, int Page = 1, int PageSize = 50);
public sealed record ProductConversionProductItem(
    Guid ProductId,
    string ProductCode,
    string? Reference,
    string ProductName,
    string UnitCode,
    decimal QuantityOnHand,
    Guid FamilyRootProductId,
    decimal ConversionFactor,
    decimal MaximumLossPercent);
public sealed record ProductConversionProductPage(IReadOnlyList<ProductConversionProductItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record InventoryWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record WarehouseMasterItem(Guid WarehouseId, string Code, string Name, bool AllowNegativeStockSales,
    string PriceFormationCostBasis, bool IsSystem, bool UseForSales, bool UseForGoodsReceipts,
    bool IsInventoryVisible, bool IsActive);
public sealed record SaveWarehouseRequest(string Name, bool AllowNegativeStockSales, string PriceFormationCostBasis,
    bool UseForSales, bool IsActive);
public sealed record InventoryReasonItem(Guid InventoryReasonId, string OperationType, string Code, string Name, bool IsSystem, bool IsActive, int DisplayOrder);
public sealed record SaveInventoryReasonRequest(string OperationType, string Name, bool IsActive, int DisplayOrder);
public sealed record InventoryBalanceQuery(Guid BusinessId, Guid? WarehouseId, string? Search, bool OnlyWithStock, int Page = 1, int PageSize = 50, Guid? ProductId = null);
public sealed record InventoryBalanceItem(Guid WarehouseId, string WarehouseCode, string WarehouseName, Guid ProductId, string ProductCode, string ProductName, bool ManagesInventory, decimal QuantityOnHand, decimal? UnitCost, decimal? AverageUnitCost, decimal? InventoryValue, DateTimeOffset? UpdatedAt);
public sealed record InventoryBalancePage(IReadOnlyList<InventoryBalanceItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record InventoryMovementQuery(Guid BusinessId, Guid? WarehouseId, Guid? ProductId, string? Search, string? DocumentType, string? MovementType, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 50);
public sealed record InventoryMovementItem(Guid InventoryMovementId, Guid WarehouseId, string WarehouseName, Guid ProductId, string ProductCode, string ProductName, Guid DocumentId, string DocumentType, string? DocumentNumber, string MovementType, decimal QuantityChange, decimal QuantityBefore, decimal QuantityAfter, decimal? RecognizedUnitCost, decimal? ValueChange, DateTimeOffset OccurredAt, DateTimeOffset? PostedAt);
public sealed record InventoryMovementPage(IReadOnlyList<InventoryMovementItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record InventoryOperationQuery(
    Guid BusinessId, Guid? WarehouseId, string? Search, string? DocumentType,
    string? Status, DateTimeOffset? From, DateTimeOffset? To,
    string? ReasonCode, Guid? DestinationWarehouseId, Guid? SupplierId,
    string? PurchaseEvidenceType, int Page = 1, int PageSize = 50);
public sealed record InventoryOperationItem(
    Guid DocumentId, string DocumentType, string? DocumentNumber, Guid WarehouseId,
    string WarehouseName, Guid? DestinationWarehouseId, string? DestinationWarehouseName,
    string ReasonCode, string Status, DateTimeOffset OccurredAt, int LineCount,
    decimal? TotalValueChange, decimal? ConversionInputEquivalent = null,
    decimal? ConversionOutputEquivalent = null, decimal? ConversionLossQuantity = null,
    decimal? ConversionLossPercent = null, decimal? ConversionMaximumLossPercent = null);
public sealed record InventoryOperationDetailLine(
    int LineNumber, string Direction, Guid ProductId, string ProductCode,
    string ProductName, decimal? Quantity, decimal? PreCountQuantity, decimal? SystemQuantityAtBase,
    decimal? ExplicitUnitCost, decimal? AllocationWeight,
    decimal? ProcessedUnitCost, decimal? ProcessedValue,
    decimal? ConversionFactor = null,
    decimal? ConversionEquivalentQuantity = null);

public sealed record InventoryOperationDetail(
    Guid DocumentId, string DocumentType, string? DocumentNumber,
    Guid WarehouseId, string WarehouseName, Guid? DestinationWarehouseId,
    string? DestinationWarehouseName, string ReasonCode, string ReasonDescription,
    string? ConversionType, long? BaseInventorySequence, string? Notes, string Status,
    DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt,
    DateTimeOffset? ProcessedAt, decimal? TotalValueChange,
    IReadOnlyList<InventoryOperationDetailLine> Lines,
    Guid? ConversionFamilyRootProductId = null,
    decimal? ConversionInputEquivalent = null,
    decimal? ConversionOutputEquivalent = null,
    decimal? ConversionLossQuantity = null,
    decimal? ConversionLossPercent = null,
    decimal? ConversionMaximumLossPercent = null);

public sealed record InventoryOperationPage(IReadOnlyList<InventoryOperationItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CreateInventoryPhysicalCountListRequest(
    Guid ListId, string Name, Guid? AssignedUserId, IReadOnlyCollection<Guid> ProductIds);
public sealed record CreateInventoryPhysicalCountRequest(
    Guid InventoryPhysicalCountId, Guid BusinessId, Guid WarehouseId, string ScopeType,
    string ReasonCode, string? Notes, IReadOnlyCollection<CreateInventoryPhysicalCountListRequest> Lists);
public sealed record InventoryPhysicalCountCaptureLine(Guid ProductId, decimal Quantity);
public sealed record SaveInventoryPhysicalCountCaptureRequest(
    Guid BusinessId, IReadOnlyCollection<InventoryPhysicalCountCaptureLine> Lines, bool Submit);
public sealed record CloseInventoryPhysicalCountRequest(Guid BusinessId);
public sealed record InventoryPhysicalCountQuery(
    Guid BusinessId, Guid? WarehouseId, string? Search, string? Status, int Page = 1, int PageSize = 50);
public sealed record InventoryPhysicalCountItem(
    Guid InventoryPhysicalCountId, Guid WarehouseId, string WarehouseName, string ScopeType,
    string ReasonCode, string Status, int ListCount, int ProductCount, int PreCountedCount,
    int CountedCount, int DifferenceCount, DateTimeOffset CreatedAt, string? FinalDocumentNumber);
public sealed record InventoryPhysicalCountPage(
    IReadOnlyList<InventoryPhysicalCountItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record InventoryPhysicalCountLine(
    Guid ProductId, string ProductCode, string ProductName, decimal SystemQuantityAtBase,
    decimal? PreCountQuantity, decimal? CountedQuantity, decimal? ExpectedQuantityAtCount,
    decimal? ApprovedDifference, bool IsExcluded, string? ExclusionReason);
public sealed record InventoryPhysicalCountList(
    Guid ListId, string Name, Guid? AssignedUserId, string Status,
    DateTimeOffset? PreCountSubmittedAt, DateTimeOffset? CountSubmittedAt,
    IReadOnlyList<InventoryPhysicalCountLine> Lines);
public sealed record InventoryPhysicalCountDetail(
    Guid InventoryPhysicalCountId, Guid WarehouseId, string WarehouseName, string ScopeType,
    string ReasonCode, string? Notes, long BaseInventorySequence, string Status,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? ReviewStartedAt, DateTimeOffset? ClosedAt,
    Guid? FinalInventoryOperationId, string? FinalDocumentNumber,
    IReadOnlyList<InventoryPhysicalCountList> Lists);
public sealed record InventoryPhysicalCountClosePreparation(
    Guid InventoryPhysicalCountId, Guid BusinessId, Guid WarehouseId, string ReasonCode,
    string? Notes, Guid FinalInventoryOperationId,
    IReadOnlyList<InventoryPhysicalCountCloseLine> Lines);
public sealed record InventoryPhysicalCountCloseLine(
    Guid ProductId, decimal PreCountQuantity, decimal AdjustedCountQuantity);
public static class InventoryOperationContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(InventoryOperationDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static InventoryOperationDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<InventoryOperationDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The inventory operation payload is invalid.");
}
