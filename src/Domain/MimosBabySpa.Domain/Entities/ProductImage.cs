namespace MimosBabySpa.Domain.Entities;

public sealed class ProductImage
{
    public Guid ProductImageId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ProductOfferId { get; set; }
    public string MediaUrl { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Product Product { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public ProductOffer? ProductOffer { get; set; }
}
