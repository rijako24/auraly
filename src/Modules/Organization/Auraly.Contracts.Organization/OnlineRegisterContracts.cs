namespace Auraly.Contracts.Organization;

public sealed record OnlineRegisterOption(
    Guid BusinessId, string BusinessName,
    Guid LocationId, string LocationCode, string LocationName,
    Guid RegisterId, string RegisterCode, string RegisterName,
    Guid WarehouseId, string WarehouseCode, string WarehouseName,
    bool WarehouseAllowsNegativeStockSales,
    bool HasActiveEdgeEnrollment);

public sealed record OnlineRegisterSelection(
    Guid BusinessId,
    Guid LocationId,
    Guid RegisterId);

public sealed record OnlineRegisterContext(
    Guid BusinessId, string BusinessName,
    Guid LocationId, string LocationCode, string LocationName,
    Guid RegisterId, string RegisterCode, string RegisterName,
    Guid WarehouseId, string WarehouseCode, string WarehouseName,
    bool WarehouseAllowsNegativeStockSales);
