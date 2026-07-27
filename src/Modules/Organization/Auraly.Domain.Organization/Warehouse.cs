using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Organization;

public sealed class Warehouse
{
    public Warehouse(
        WarehouseId id,
        BusinessId businessId,
        LocationId locationId,
        string code,
        string name,
        bool allowNegativeStockSales)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("A warehouse ID is required.", nameof(id));
        if (businessId.Value == Guid.Empty) throw new ArgumentException("A business ID is required.", nameof(businessId));
        if (locationId.Value == Guid.Empty) throw new ArgumentException("A location ID is required.", nameof(locationId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A warehouse code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A warehouse name is required.", nameof(name));

        Id = id;
        BusinessId = businessId;
        LocationId = locationId;
        Code = code.Trim();
        Name = name.Trim();
        AllowNegativeStockSales = allowNegativeStockSales;
    }

    public WarehouseId Id { get; }
    public BusinessId BusinessId { get; }
    public LocationId LocationId { get; }
    public string Code { get; }
    public string Name { get; private set; }
    public bool AllowNegativeStockSales { get; private set; }

    public void SetNegativeStockPolicy(bool allow) => AllowNegativeStockSales = allow;
}
