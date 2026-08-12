using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public ProductCategoryRepository(ApplicationDbContext context) => _context = context;

    public Task<ProductCategory?> GetByIdAsync(
        Guid businessId,
        Guid productCategoryId,
        CancellationToken ct = default) =>
        _context.ProductCategories.FirstOrDefaultAsync(category =>
            category.BusinessId == businessId
            && category.ProductCategoryId == productCategoryId,
            ct);

    public async Task<IReadOnlyList<ProductCategory>> ListAsync(
        Guid businessId,
        bool includeInactive,
        CancellationToken ct = default) =>
        await _context.ProductCategories.AsNoTracking()
            .Where(category => category.BusinessId == businessId
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
            category.BusinessId == businessId
            && category.IntegrationConnectionId == integrationConnectionId
            && category.ExternalCategoryId == externalCategoryId,
            ct);

    public Task<ProductCategory?> GetByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default) =>
        _context.ProductCategories.FirstOrDefaultAsync(category =>
            category.BusinessId == businessId
            && category.IntegrationConnectionId == integrationConnectionId
            && category.Name == name,
            ct);

    public Task<ProductCategory?> FindBrowsableByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default) =>
        _context.ProductCategories.AsNoTracking().FirstOrDefaultAsync(category =>
            category.BusinessId == businessId
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
            .Where(category => category.BusinessId == businessId
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

    public Task<ProductCategory> UpdateAsync(ProductCategory category, CancellationToken ct = default)
    {
        _context.ProductCategories.Update(category);
        return Task.FromResult(category);
    }
}
