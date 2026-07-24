using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed partial class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Product>> SearchAsync(
        Guid businessId,
        string? query,
        string? category,
        int limit,
        CancellationToken ct = default,
        bool includeInactive = false)
    {
        limit = Math.Clamp(limit, 1, 50);
        var products = _context.Products
            .AsNoTracking()
            .Where(p => p.BusinessId == businessId);

        if (!includeInactive)
            products = products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(category))
            {
                var searchTerm = term;
                products = products.Where(p => p.CategoryName != null && p.CategoryName.Contains(searchTerm));
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(query))
            {
                var searchTerm = term;
                products = products.Where(p =>
                    p.Name.Contains(searchTerm) ||
                    (p.Sku != null && p.Sku.Contains(searchTerm)) ||
                    (p.Description != null && p.Description.Contains(searchTerm)) ||
                    (p.CategoryName != null && p.CategoryName.Contains(searchTerm)));
            }
        }

        return await products
            .OrderBy(p => p.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchPageAsync(
        Guid businessId,
        string? query,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct = default,
        bool includeInactive = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var products = _context.Products
            .AsNoTracking()
            .Where(product => product.BusinessId == businessId);

        if (!includeInactive)
            products = products.Where(product => product.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(category))
            {
                var searchTerm = term;
                products = products.Where(product =>
                    product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(query))
            {
                var searchTerm = term;
                products = products.Where(product =>
                    product.Name.Contains(searchTerm)
                    || product.Sku != null && product.Sku.Contains(searchTerm)
                    || product.Description != null && product.Description.Contains(searchTerm)
                    || product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }

        var totalCount = await products.CountAsync(ct);
        var items = await products
            .OrderBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);

    }
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search = null,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.BusinessId == businessId);

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(search))
            {
                var searchTerm = term;
                query = query.Where(p =>
                    p.Name.Contains(searchTerm) ||
                    (p.Sku != null && p.Sku.Contains(searchTerm)) ||
                    (p.Description != null && p.Description.Contains(searchTerm)) ||
                    (p.CategoryName != null && p.CategoryName.Contains(searchTerm)));
            }
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<Product?> GetByIdAsync(Guid businessId, Guid productId, CancellationToken ct = default) =>
        _context.Products.FirstOrDefaultAsync(p => p.BusinessId == businessId && p.ProductId == productId, ct);

    public Task<Product?> GetByExternalIdAsync(Guid businessId, Guid integrationConnectionId, string externalProductId, CancellationToken ct = default) =>
        _context.Products.FirstOrDefaultAsync(p =>
            p.BusinessId == businessId &&
            p.IntegrationConnectionId == integrationConnectionId &&
            p.ExternalProductId == externalProductId,
            ct);

    public Task<Product> CreateAsync(Product product, CancellationToken ct = default)
    {
        _context.Products.Add(product);
        return Task.FromResult(product);
    }

    public Task<Product> UpdateAsync(Product product, CancellationToken ct = default)
    {
        _context.Products.Update(product);
        return Task.FromResult(product);
    }
}
