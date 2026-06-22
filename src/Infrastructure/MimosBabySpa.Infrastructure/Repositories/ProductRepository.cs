using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Product>> SearchAsync(
        Guid businessId,
        string? query,
        string? category,
        int limit,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var products = _context.Products
            .AsNoTracking()
            .Where(p => p.BusinessId == businessId && p.IsActive);

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
