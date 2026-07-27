using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Organization;
using Auraly.Domain.Organization;

namespace Auraly.Application.Organization;

public sealed class RegisterContextProjector
{
    public RegisterContext Project(TenantId tenantId, Register register, Warehouse warehouse)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(warehouse);
        if (register.BusinessId != warehouse.BusinessId || register.WarehouseId != warehouse.Id)
        {
            throw new InvalidOperationException("The register is not assigned to the supplied warehouse.");
        }

        return new RegisterContext(
            tenantId,
            register.BusinessId,
            warehouse.LocationId,
            warehouse.Id,
            register.Id,
            warehouse.AllowNegativeStockSales);
    }
}
