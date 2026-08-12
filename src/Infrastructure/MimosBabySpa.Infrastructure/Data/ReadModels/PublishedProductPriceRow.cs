namespace MimosBabySpa.Infrastructure.Data.ReadModels;

/// <summary>
/// Read/write mapping for the canonical published selling price. It maps the
/// existing Auraly table and is deliberately separate from the product master.
/// </summary>
public sealed class PublishedProductPriceRow
{
    public Guid ProductPriceId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "COP";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
