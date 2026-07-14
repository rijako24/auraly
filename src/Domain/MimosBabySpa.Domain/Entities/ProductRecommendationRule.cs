using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class ProductRecommendationRule
{
    public Guid ProductRecommendationRuleId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public ProductRecommendationMatchType MatchType { get; set; }
    public Guid? SourceProductId { get; set; }
    public string? SourceValue { get; set; }
    public Guid? RecommendedProductId { get; set; }
    public string? RecommendedSearchText { get; set; }
    public string? RecommendedExternalProductId { get; set; }
    public string? RecommendedSku { get; set; }
    public ProductRecommendationType RecommendationType { get; set; } = ProductRecommendationType.Complement;
    public int Priority { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
    public virtual Product? SourceProduct { get; set; }
    public virtual Product? RecommendedProduct { get; set; }
}
