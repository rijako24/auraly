using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Product
{
    public Guid ProductId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public string? ExternalProductId { get; set; }
    public ProductSource Source { get; set; } = ProductSource.Local;
    public string? Sku { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "COP";
    public bool ManageStock { get; set; }
    public decimal? StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RawPayloadJson { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
}
