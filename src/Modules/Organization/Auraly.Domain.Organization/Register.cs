using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Organization;

public sealed class Register
{
    public Register(RegisterId id, BusinessId businessId, WarehouseId warehouseId, string code)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("A register ID is required.", nameof(id));
        if (businessId.Value == Guid.Empty) throw new ArgumentException("A business ID is required.", nameof(businessId));
        if (warehouseId.Value == Guid.Empty) throw new ArgumentException("A warehouse ID is required.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A register code is required.", nameof(code));

        Id = id;
        BusinessId = businessId;
        WarehouseId = warehouseId;
        Code = code.Trim();
    }

    public RegisterId Id { get; }
    public BusinessId BusinessId { get; }
    public WarehouseId WarehouseId { get; private set; }
    public string Code { get; }

    public void AssignWarehouse(Warehouse warehouse)
    {
        if (warehouse.BusinessId != BusinessId)
        {
            throw new InvalidOperationException("A register and warehouse must belong to the same business.");
        }

        WarehouseId = warehouse.Id;
    }
}
