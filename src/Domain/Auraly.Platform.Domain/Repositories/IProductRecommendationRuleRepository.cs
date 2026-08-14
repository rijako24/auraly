using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IProductRecommendationRuleRepository
{
    Task<IReadOnlyList<ProductRecommendationRule>> GetActiveAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        DateTime utcNow,
        CancellationToken ct = default);
}
