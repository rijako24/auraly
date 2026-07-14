using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ProductRecommendationRuleRepository : IProductRecommendationRuleRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRecommendationRuleRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductRecommendationRule>> GetActiveAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        return await _context.ProductRecommendationRules
            .AsNoTracking()
            .Include(rule => rule.SourceProduct)
            .Include(rule => rule.RecommendedProduct)
            .Where(rule => rule.BusinessId == businessId
                           && rule.IsActive
                           && (!rule.IntegrationConnectionId.HasValue
                               || rule.IntegrationConnectionId == integrationConnectionId)
                           && (!rule.StartsAtUtc.HasValue || rule.StartsAtUtc <= utcNow)
                           && (!rule.EndsAtUtc.HasValue || rule.EndsAtUtc > utcNow))
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.ProductRecommendationRuleId)
            .ToListAsync(ct);
    }
}
