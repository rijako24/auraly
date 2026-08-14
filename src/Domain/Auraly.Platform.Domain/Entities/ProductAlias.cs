using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public sealed class ProductAlias
{
    public Guid ProductAliasId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public ProductAliasScope Scope { get; set; }
    public string CustomerKey { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public ProductAliasKind Kind { get; set; }
    public ProductAliasResolutionMode ResolutionMode { get; set; }
    public ProductAliasSource Source { get; set; }
    public ProductAliasStatus Status { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Product Product { get; set; } = null!;
    public Business Business { get; set; } = null!;
}
