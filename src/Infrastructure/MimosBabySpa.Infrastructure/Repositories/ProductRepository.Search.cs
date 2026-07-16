using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    public Task<Product?> GetByAnyExternalIdAsync(Guid businessId, string externalProductId, CancellationToken ct = default) =>
        _context.Products.FirstOrDefaultAsync(product =>
            product.BusinessId == businessId && product.ExternalProductId == externalProductId, ct);

    public Task<Product?> GetBySkuAsync(Guid businessId, string sku, CancellationToken ct = default) =>
        _context.Products.FirstOrDefaultAsync(product =>
            product.BusinessId == businessId && product.Sku == sku, ct);

    public async Task<IReadOnlyList<Product>> GetIdentityCatalogAsync(Guid businessId, CancellationToken ct = default) =>
        await _context.Products.AsNoTracking()
            .Where(product => product.BusinessId == businessId).ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> SearchByIndexTermsAsync(
        Guid businessId,
        IReadOnlyCollection<string> terms,
        int limit,
        CancellationToken ct = default)
    {
        var keys = terms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim().ToLowerInvariant())
            .Where(term => term.Length <= 100)
            .Distinct(StringComparer.Ordinal)
            .Take(64)
            .ToArray();
        if (keys.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, 250);
        var productIds = await _context.ProductSearchTerms
            .AsNoTracking()
            .Where(term => term.BusinessId == businessId && keys.Contains(term.Term))
            .GroupBy(term => term.ProductId)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .Take(limit)
            .ToListAsync(ct);

        if (productIds.Count > 0)
        {
            return await _context.Products.AsNoTracking()
                .Where(product => product.BusinessId == businessId && product.IsActive && productIds.Contains(product.ProductId))
                .Take(limit)
                .ToListAsync(ct);
        }

        // Compatibility fallback for products created before the lexical index existed.
        var fallback = new Dictionary<Guid, Product>();
        foreach (var key in keys.OrderByDescending(value => value.Length).Take(6))
        {
            var matches = await _context.Products.AsNoTracking()
                .Where(product => product.BusinessId == businessId && product.IsActive
                    && (product.Name.Contains(key)
                        || product.Sku != null && product.Sku.Contains(key)
                        || product.ExternalProductId != null && product.ExternalProductId.Contains(key)
                        || product.CategoryName != null && product.CategoryName.Contains(key)))
                .Take(limit)
                .ToListAsync(ct);
            foreach (var product in matches)
                fallback.TryAdd(product.ProductId, product);
            if (fallback.Count >= limit)
                break;
        }
        return fallback.Values.Take(limit).ToList();
    }

    private async Task SyncSearchTermsAsync(Product product, CancellationToken ct)
    {
        var desired = ProductSearchText.GetIndexTerms(
            product.Name,
            product.Sku,
            product.ExternalProductId,
            product.CategoryName);
        var existing = await _context.ProductSearchTerms
            .Where(term => term.BusinessId == product.BusinessId && term.ProductId == product.ProductId)
            .ToListAsync(ct);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);
        var existingSet = existing.Select(term => term.Term).ToHashSet(StringComparer.Ordinal);

        _context.ProductSearchTerms.RemoveRange(existing.Where(term => !desiredSet.Contains(term.Term)));
        foreach (var term in desiredSet.Except(existingSet, StringComparer.Ordinal))
        {
            _context.ProductSearchTerms.Add(new ProductSearchTerm
            {
                BusinessId = product.BusinessId,
                ProductId = product.ProductId,
                Term = term,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
