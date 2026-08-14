namespace Auraly.Platform.Domain.Entities;

public sealed class ProductCategory
{
    public Guid ProductCategoryId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ParentProductCategoryId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public string? ExternalCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBrowsable { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
    public ProductCategory? Parent { get; set; }
    public ICollection<ProductCategory> Children { get; set; } = new List<ProductCategory>();
    public IntegrationConnection? IntegrationConnection { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
