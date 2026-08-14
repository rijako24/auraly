namespace Auraly.Platform.Domain.Entities;

public sealed class ProductOffer
{
    public Guid ProductOfferId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BusinessId { get; set; }
    public string Condition { get; set; } = "new";
    public int? StorageGb { get; set; }
    public string? Color { get; set; }
    public string? VariantLabel { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "COP";
    public int? MinimumBatteryHealthPercent { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? PriceSourceUrl { get; set; }
    public DateTime? PriceObservedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Product Product { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
