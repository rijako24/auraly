using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public ProductCategoryRepository(ApplicationDbContext context) => _context = context;

    public Task<ProductCategory?> GetByIdAsync(
        Guid businessId,
        Guid productCategoryId,
        CancellationToken ct = default) =>
        _context.ProductCategories.FirstOrDefaultAsync(category =>
            _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
            && category.ProductCategoryId == productCategoryId,
            ct);

    public async Task<IReadOnlyList<ProductCategory>> ListAsync(
        Guid businessId,
        bool includeInactive,
        CancellationToken ct = default) =>
        await _context.ProductCategories.AsNoTracking()
            .Where(category => _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
                && (includeInactive || category.IsActive))
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(ct);
    public Task<ProductCategory?> GetByExternalIdAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string externalCategoryId,
        CancellationToken ct = default) =>
        _context.ProductCategories.FirstOrDefaultAsync(category =>
            _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
            && category.IntegrationConnectionId == integrationConnectionId
            && category.ExternalCategoryId == externalCategoryId,
            ct);

    public async Task<IReadOnlyList<ProductCategory>> GetForExternalSyncAsync(
        Guid tenantId,
        Guid integrationConnectionId,
        IReadOnlyCollection<string> externalCategoryIds,
        IReadOnlyCollection<string> names,
        CancellationToken ct = default)
    {
        var ids = externalCategoryIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var categoryNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 && categoryNames.Length == 0) return [];
        return await _context.ProductCategories
            .Where(category => category.TenantId == tenantId
                && category.IntegrationConnectionId == integrationConnectionId
                && (category.ExternalCategoryId != null && ids.Contains(category.ExternalCategoryId)
                    || categoryNames.Contains(category.Name)))
            .ToListAsync(ct);
    }

    public Task<ProductCategory?> GetByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default) =>
        _context.ProductCategories.FirstOrDefaultAsync(category =>
            _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
            && category.IntegrationConnectionId == integrationConnectionId
            && category.Name == name,
            ct);

    public Task<ProductCategory?> FindBrowsableByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default) =>
        _context.ProductCategories.AsNoTracking().FirstOrDefaultAsync(category =>
            _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
            && category.IntegrationConnectionId == integrationConnectionId
            && category.IsActive
            && category.IsBrowsable
            && category.Name == name,
            ct);

    public async Task<(IReadOnlyList<ProductCategory> Items, int TotalCount)> GetBrowsablePageAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = _context.ProductCategories.AsNoTracking()
            .Where(category => _context.Businesses.Any(business => business.BusinessId == businessId && business.TenantId == category.TenantId)
                && category.IntegrationConnectionId == integrationConnectionId
                && category.IsActive
                && category.IsBrowsable);
        var count = await query.CountAsync(ct);
        var items = await query
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, count);
    }

    public Task<ProductCategory> CreateAsync(ProductCategory category, CancellationToken ct = default)
    {
        _context.ProductCategories.Add(category);
        return Task.FromResult(category);
    }

    public Task CreateManyAsync(IReadOnlyCollection<ProductCategory> categories, CancellationToken ct = default)
    {
        _context.ProductCategories.AddRange(categories);
        return Task.CompletedTask;
    }

    public Task<ProductCategory> UpdateAsync(ProductCategory category, CancellationToken ct = default)
    {
        _context.ProductCategories.Update(category);
        return Task.FromResult(category);
    }
}
