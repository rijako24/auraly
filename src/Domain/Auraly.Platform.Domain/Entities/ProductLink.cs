namespace Auraly.Platform.Domain.Entities;

public sealed class ProductLink
{
    public Guid ProductLinkId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ChildProductId { get; set; }
    public Guid ParentProductId { get; set; }
    public decimal? InventoryFactor { get; set; }
    public decimal? PriceFactor { get; set; }
    public decimal? ConversionFactor { get; set; }
    public bool SharesInventory { get; set; }
    public bool SharesPrice { get; set; }
    public bool AllowsConversion { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
