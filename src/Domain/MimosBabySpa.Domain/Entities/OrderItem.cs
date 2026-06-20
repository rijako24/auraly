namespace MimosBabySpa.Domain.Entities;

public class OrderItem
{
    public Guid OrderItemId { get; set; }
    public Guid OrderId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public string? ExternalProductId { get; set; }
    public string? Sku { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? DescriptionSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string? RawPayloadJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Order Order { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
    public virtual Product? Product { get; set; }
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
}
