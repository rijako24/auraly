using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    public async Task<Product?> GetByAnyExternalIdAsync(Guid businessId, string externalProductId, CancellationToken ct = default)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(item =>
            item.BusinessId == businessId && item.ExternalProductId == externalProductId, ct);
        if (product is not null)
            await ApplyPublishedPricesAsync([product], businessId, ct);
        return product;
    }

    public async Task<Product?> GetBySkuAsync(Guid businessId, string sku, CancellationToken ct = default)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Sku == sku, ct);
        if (product is not null)
            await ApplyPublishedPricesAsync([product], businessId, ct);
        return product;
    }

    public async Task<IReadOnlyList<Product>> GetIdentityCatalogAsync(Guid businessId, CancellationToken ct = default)
    {
        var products = await _context.Products.AsNoTracking().Where(product => product.BusinessId == businessId).ToListAsync(ct);
        await ApplyPublishedPricesAsync(products, businessId, ct);
        return products;
    }

    public async Task<IReadOnlyList<string>> GetSearchTermsAsync(Guid businessId, Guid productId, CancellationToken ct = default) =>
        await _context.ProductSearchTerms.AsNoTracking()
            .Where(term => term.BusinessId == businessId && term.ProductId == productId)
            .OrderBy(term => term.Term)
            .Select(term => term.Term)
            .ToListAsync(ct);

    public async Task ReplaceSearchTermsAsync(Product product, CancellationToken ct = default)
    {
        var existing = await _context.ProductSearchTerms
            .Where(term => term.BusinessId == product.BusinessId && term.ProductId == product.ProductId)
            .ToListAsync(ct);
        var desired = ProductSearchText.GetProductIndexTerms(product.Name, product.Sku, product.ExternalProductId, product.CategoryName)
            .Where(term => term.Length <= 100)
            .ToHashSet(StringComparer.Ordinal);
        _context.ProductSearchTerms.RemoveRange(existing.Where(term => !desired.Contains(term.Term)));
        var existingTerms = existing.Select(term => term.Term).ToHashSet(StringComparer.Ordinal);
        _context.ProductSearchTerms.AddRange(desired.Where(term => !existingTerms.Contains(term)).Select(term => new ProductSearchTerm
        {
            BusinessId = product.BusinessId,
            ProductId = product.ProductId,
            Term = term,
            CreatedAt = DateTime.UtcNow
        }));
    }

    public async Task<IReadOnlyList<Product>> SearchByIndexTermsAsync(Guid businessId, IReadOnlyCollection<string> terms, int limit, CancellationToken ct = default)
    {
        var keys = terms.Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim().ToLowerInvariant()).Where(term => term.Length <= 100)
            .Distinct(StringComparer.Ordinal).Take(64).ToArray();
        if (keys.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, 250);
        var indexedMatches = await _context.ProductSearchTerms.AsNoTracking()
            .Where(term => term.BusinessId == businessId && keys.Contains(term.Term))
            .GroupBy(term => term.ProductId).OrderByDescending(group => group.Count())
            .Select(group => new { ProductId = group.Key, Hits = group.Count() })
            .Take(Math.Min(limit * 4, 1000)).ToListAsync(ct);
        var indexedScores = indexedMatches.ToDictionary(match => match.ProductId, match => match.Hits);
        var indexedIds = indexedScores.Keys.ToArray();
        var candidates = indexedIds.Length == 0
            ? new Dictionary<Guid, Product>()
            : (await _context.Products.AsNoTracking().Where(product => product.BusinessId == businessId && indexedIds.Contains(product.ProductId)).ToListAsync(ct))
                .ToDictionary(product => product.ProductId);

        foreach (var key in keys.OrderByDescending(value => value.Length).Take(6))
        {
            var matches = await _context.Products.AsNoTracking()
                .Where(product => product.BusinessId == businessId
                    && (product.Name.ToLower().Contains(key)
                        || product.Sku != null && product.Sku.ToLower().Contains(key)
                        || product.ExternalProductId != null && product.ExternalProductId.ToLower().Contains(key)
                        || product.CategoryName != null && product.CategoryName.ToLower().Contains(key)))
                .Take(limit).ToListAsync(ct);
            foreach (var product in matches)
                candidates.TryAdd(product.ProductId, product);
        }

        await ApplyPublishedPricesAsync(candidates.Values.ToList(), businessId, ct);
        return candidates.Values.Where(product => product.HasPublishedPrice).Select(product => new
        {
            Product = product,
            DirectScore = DirectIdentityScore(product, keys),
            IndexScore = indexedScores.GetValueOrDefault(product.ProductId)
        }).OrderByDescending(candidate => candidate.Product.IsActive)
          .ThenByDescending(candidate => candidate.DirectScore)
          .ThenByDescending(candidate => candidate.IndexScore)
          .ThenBy(candidate => candidate.Product.Name)
          .Take(limit)
          .Select(candidate => candidate.Product)
          .ToList();
    }

    private static int DirectIdentityScore(Product product, IReadOnlyCollection<string> keys) =>
        keys.Count(ProductSearchText.GetProductIndexTerms(product.Name, product.Sku, product.ExternalProductId, product.CategoryName).Contains);
}
