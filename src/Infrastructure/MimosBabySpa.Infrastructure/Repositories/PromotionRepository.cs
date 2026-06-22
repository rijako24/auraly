using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Extensions;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class PromotionRepository : IPromotionRepository
{
    private readonly ApplicationDbContext _context;

    public PromotionRepository(ApplicationDbContext context) => _context = context;

    public Task<Promotion?> GetByIdAsync(Guid businessId, Guid promotionId, CancellationToken ct = default) =>
        WithDetails()
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.PromotionId == promotionId, ct);

    public async Task<IReadOnlyList<Promotion>> GetActiveByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default) =>
        await WithDetails()
            .AsNoTracking()
            .Where(p =>
                p.BusinessId == businessId &&
                p.IsActive &&
                (p.StartsAtUtc == null || p.StartsAtUtc <= utcNow) &&
                (p.EndsAtUtc == null || p.EndsAtUtc >= utcNow))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default)
    {
        var query = WithDetails()
            .AsNoTracking()
            .Where(p => p.BusinessId == businessId);

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
        _context.Promotions.Update(promotion);
        return Task.FromResult(promotion);
    }

    private IQueryable<Promotion> WithDetails() =>
        _context.Promotions
            .Include(p => p.Conditions)
            .Include(p => p.Benefits);
}
