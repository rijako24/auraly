using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.Organization;

public sealed record SalesExecutionContext(
    TenantId TenantId,
    BusinessId BusinessId,
    WarehouseId WarehouseId,
    UserId UserId,
    DeviceId? DeviceId,
    WorkSessionId WorkSessionId,
    bool WarehouseAllowsNegativeStockSales);