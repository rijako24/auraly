namespace Auraly.Platform.Infrastructure.Data.ReadModels;

/// <summary>Read-only warehouse scope used by EF inventory projections.</summary>
public sealed class InventoryWarehouseScopeRow
{
    public Guid WarehouseId { get; set; }
    public Guid BusinessId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}
