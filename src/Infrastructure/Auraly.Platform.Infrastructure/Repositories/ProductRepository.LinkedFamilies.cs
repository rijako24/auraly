using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    public async Task<IReadOnlyList<Product>> GetLinkedFamilyAsync(
        Guid businessId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct = default)
    {
        var seeds = productIds.Distinct().ToArray();
        if (seeds.Length == 0) return [];

        var roots = await _context.ProductLinks.AsNoTracking()
            .Where(link => link.BusinessId == businessId
                && link.IsActive
                && (seeds.Contains(link.ChildProductId) || seeds.Contains(link.ParentProductId)))
            .Select(link => link.ParentProductId)
            .Distinct()
            .ToListAsync(ct);

        var familyIds = new HashSet<Guid>(seeds);
        foreach (var root in roots) familyIds.Add(root);
        if (roots.Count > 0)
        {
            var children = await _context.ProductLinks.AsNoTracking()
                .Where(link => link.BusinessId == businessId
                    && link.IsActive
                    && roots.Contains(link.ParentProductId))
                .Select(link => link.ChildProductId)
                .ToListAsync(ct);
            foreach (var child in children) familyIds.Add(child);
        }

        var now = DateTimeOffset.UtcNow;
        var ids = familyIds.ToArray();
        var products = await _context.Products.AsNoTracking()
            .Where(product => product.BusinessId == businessId
                && product.IsActive
                && ids.Contains(product.ProductId)
                && _context.PublishedProductPrices.Any(price =>
                    price.BusinessId == businessId
                    && price.ProductId == product.ProductId
                    && price.IsActive
                    && price.ValidFrom <= now
                    && (price.ValidUntil == null || price.ValidUntil > now)))
            .OrderBy(product => product.Name)
            .ToListAsync(ct);

        await ApplyPublishedPricesAsync(products, businessId, ct);
        return products.Where(product => product.HasPublishedPrice).ToList();
    }
}
