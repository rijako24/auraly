namespace Auraly.Platform.Domain.Entities;

public sealed class ProductSearchTerm
{
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public string Term { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
    public Business Business { get; set; } = null!;
}
