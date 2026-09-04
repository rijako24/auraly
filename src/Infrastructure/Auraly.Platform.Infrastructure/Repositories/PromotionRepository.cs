using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class PromotionRepository : IPromotionRepository
{
    private readonly ApplicationDbContext _context;

    public PromotionRepository(ApplicationDbContext context) => _context = context;

    public async Task<Promotion?> GetByIdAsync(Guid businessId, Guid promotionId, CancellationToken ct = default)
    {
        var tenantId = await TenantIdAsync(businessId, ct);
        return await WithDetails().FirstOrDefaultAsync(p =>
            p.PromotionId == promotionId
            && p.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<Promotion>> GetActiveByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default)
    {
        var tenantId = await TenantIdAsync(businessId, ct);
        return await WithDetails()
            .AsNoTracking()
            .Where(p =>
                p.TenantId == tenantId &&
                (p.AppliesToAllBusinesses || p.BusinessScopes.Any(scope => scope.BusinessId == businessId)) &&
                p.IsActive &&
                (p.StartsAtUtc == null || p.StartsAtUtc <= utcNow) &&
                (p.EndsAtUtc == null || p.EndsAtUtc >= utcNow))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default)
    {
        var tenantId = await TenantIdAsync(businessId, ct);
        var query = WithDetails()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                (p.Description != null && p.Description.Contains(term)) ||
                (p.CouponCode != null && p.CouponCode.Contains(term)));
        }

        return await query
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .ToPagedListAsync(page, pageSize, ct);
    }

    public Task<Promotion> CreateAsync(Promotion promotion, CancellationToken ct = default)
    {
        _context.Promotions.Add(promotion);
        return Task.FromResult(promotion);
    }

    public Task<Promotion> UpdateAsync(Promotion promotion, CancellationToken ct = default)
    {
        // Promotion aggregates are loaded tracked with their details. Calling Update here
        // marks newly replaced conditions/benefits as Modified instead of Added and causes
        // a false optimistic-concurrency failure for their generated keys.
        MarkNewChildrenAdded(promotion.Conditions);
        MarkNewChildrenAdded(promotion.Benefits);
        MarkNewChildrenAdded(promotion.BusinessScopes);
        return Task.FromResult(promotion);
    }

    private void MarkNewChildrenAdded<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        foreach (var entity in entities)
        {
            var entry = _context.Entry(entity);
            if (entry.State == EntityState.Detached)
                entry.State = EntityState.Added;
        }
    }

    private IQueryable<Promotion> WithDetails() =>
        _context.Promotions
            .Include(p => p.Conditions)
            .Include(p => p.Benefits)
            .Include(p => p.BusinessScopes);

    private async Task<Guid> TenantIdAsync(Guid businessId, CancellationToken ct) =>
        await _context.Businesses.Where(business => business.BusinessId == businessId)
            .Select(business => business.TenantId)
            .SingleAsync(ct);
}
