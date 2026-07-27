using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.Organization;

public sealed record RegisterContext(
    TenantId TenantId,
    BusinessId BusinessId,
    LocationId LocationId,
    WarehouseId WarehouseId,
    RegisterId RegisterId,
    bool WarehouseAllowsNegativeStockSales);
