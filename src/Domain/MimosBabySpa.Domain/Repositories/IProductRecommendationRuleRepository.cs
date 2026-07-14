using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IProductRecommendationRuleRepository
{
    Task<IReadOnlyList<ProductRecommendationRule>> GetActiveAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        DateTime utcNow,
        CancellationToken ct = default);
}
