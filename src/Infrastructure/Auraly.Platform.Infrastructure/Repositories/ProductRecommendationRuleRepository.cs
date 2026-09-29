using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

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
            .Where(rule => _context.Businesses.Any(b => b.BusinessId == businessId && b.TenantId == rule.TenantId)
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
