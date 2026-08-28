namespace Auraly.Platform.Infrastructure.Data.ReadModels;

/// <summary>EF projection of the canonical current inventory balance.</summary>
public sealed class InventoryBalanceRow
{
    public Guid BusinessId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal AverageUnitCost { get; set; }
    public decimal InventoryValue { get; set; }
    public long LastProcessingSequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
