using Microsoft.EntityFrameworkCore;
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
            var normalizedCategory = category.Trim();
            products = products.Where(p => p.CategoryName != null && p.CategoryName.Contains(normalizedCategory));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim();
            products = products.Where(p =>
                p.Name.Contains(normalizedQuery) ||
                (p.Sku != null && p.Sku.Contains(normalizedQuery)) ||
                (p.Description != null && p.Description.Contains(normalizedQuery)) ||
                (p.CategoryName != null && p.CategoryName.Contains(normalizedQuery)));
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
