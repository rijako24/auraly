using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Service?> GetByIdAsync(Guid serviceId)
    {
        return await _context.Services
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .FirstOrDefaultAsync(s => s.ServiceId == serviceId);
    }

    public async Task<Service?> GetByBusinessIdAndNameAsync(Guid businessId, string serviceName)
    {
        return await _context.Services
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .FirstOrDefaultAsync(s => s.BusinessId == businessId &&
                                     s.ServiceName == serviceName &&
                                     s.IsActive);
    }

    public async Task<IEnumerable<Service>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Services
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Service>> GetActiveByBusinessIdAsync(Guid businessId)
    {
        return await IncludeCatalogGraph(_context.Services)
            .Where(s => s.BusinessId == businessId && s.IsActive)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Service>> SearchActiveCatalogAsync(
        Guid businessId,
        IReadOnlyList<string> terms,
        int limit,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);
        var searchTerms = terms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        var query = IncludeCatalogGraph(_context.Services)
            .Where(s => s.BusinessId == businessId && s.IsActive);

        if (searchTerms.Count > 0)
            query = query.Where(BuildCatalogSearchPredicate(searchTerms));

        return await query
            .OrderBy(s => s.ServiceName)
            .Take(safeLimit)
            .ToListAsync(ct);
    }

    private static IQueryable<Service> IncludeCatalogGraph(IQueryable<Service> query) =>
        query
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService);

    private static Expression<Func<Service, bool>> BuildCatalogSearchPredicate(IReadOnlyList<string> terms)
    {
        var service = Expression.Parameter(typeof(Service), "s");
        Expression? body = null;

        foreach (var term in terms)
        {
            var termBody = OrElse(
                ContainsTerm(Expression.Property(service, nameof(Service.ServiceName)), term),
                ContainsTerm(Expression.Property(service, nameof(Service.Description)), term),
                ContainsTerm(Expression.Property(service, nameof(Service.Keywords)), term),
                ContainsTerm(Expression.Property(Expression.Property(service, nameof(Service.ServiceCategory)), nameof(ServiceCategory.Name)), term),
                ContainsTerm(Expression.Property(Expression.Property(service, nameof(Service.ServiceCategory)), nameof(ServiceCategory.Description)), term));

            body = body is null ? termBody : Expression.OrElse(body, termBody);
        }

        return Expression.Lambda<Func<Service, bool>>(body ?? Expression.Constant(true), service);
    }

    private static Expression OrElse(params Expression[] expressions) =>
        expressions.Aggregate(Expression.OrElse);

    private static Expression ContainsTerm(Expression value, string term)
    {
        var coalesced = Expression.Coalesce(value, Expression.Constant(string.Empty));
        var lowered = Expression.Call(coalesced, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        return Expression.Call(
            lowered,
            typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
            Expression.Constant(term));
    }

    public async Task<(IReadOnlyList<Service> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _context.Services
            .Include(s => s.ServiceCategory)
            .Where(s => s.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(svc =>
                svc.ServiceName.ToLower().Contains(s) ||
                svc.Description.ToLower().Contains(s) ||
                (svc.Keywords != null && svc.Keywords.ToLower().Contains(s)));
        }

        return await query.OrderBy(svc => svc.ServiceName).ToPagedListAsync(page, pageSize, ct);
    }

    public Task<Service> CreateAsync(Service service)
    {
        _context.Services.Add(service);
        return Task.FromResult(service);
    }

    public Task<Service> UpdateAsync(Service service)
    {
        _context.Services.Update(service);
        return Task.FromResult(service);
    }
}
