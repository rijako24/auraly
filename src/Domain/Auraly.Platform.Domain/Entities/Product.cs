using System.ComponentModel.DataAnnotations.Schema;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class Product
{
    public Guid ProductId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ProductCategoryId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public string? ExternalProductId { get; set; }
    public ProductSource Source { get; set; } = ProductSource.Local;
    public string? Sku { get; set; }
    public string? ProductCode { get; set; }
    public string? Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Set by the canonical ProductPrices reader; never persisted on Products.</summary>
    [NotMapped]
    public bool HasPublishedPrice { get; set; }
    public string Currency { get; set; } = "COP";
    public bool ManageStock { get; set; }
    public decimal? StockQuantity { get; set; }
    public decimal? ConversionMaximumLossPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RawPayloadJson { get; set; }
    public int SearchIndexVersion { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public virtual ProductCategory? ProductCategory { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
    public virtual ICollection<ProductAlias> Aliases { get; set; } = new List<ProductAlias>();
    public virtual ICollection<ProductSearchTerm> SearchTerms { get; set; } = new List<ProductSearchTerm>();
    public virtual ICollection<ProductOffer> Offers { get; set; } = new List<ProductOffer>();
    public virtual ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
