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
        var now = DateTimeOffset.UtcNow;
        var products = _context.Products.AsNoTracking().Where(product =>
            product.BusinessId == businessId
            && _context.PublishedProductPrices.Any(price =>
                price.BusinessId == businessId
                && price.ProductId == product.ProductId
                && price.IsActive
                && price.ValidFrom <= now
                && (price.ValidUntil == null || price.ValidUntil > now)));
        if (!includeInactive)
            products = products.Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(category))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(category))
            {
                var searchTerm = term;
                products = products.Where(product => product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(query))
            {
                var searchTerm = term;
                products = products.Where(product => product.Name.Contains(searchTerm)
                    || product.Sku != null && product.Sku.Contains(searchTerm)
                    || product.Description != null && product.Description.Contains(searchTerm)
                    || product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }
        var items = await products.OrderBy(product => product.Name).Take(limit).ToListAsync(ct);
        await ApplyPublishedPricesAsync(items, businessId, ct);
        return items.Where(product => product.HasPublishedPrice).ToList();
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
        var now = DateTimeOffset.UtcNow;
        var products = _context.Products.AsNoTracking().Where(product =>
            product.BusinessId == businessId
            && _context.PublishedProductPrices.Any(price =>
                price.BusinessId == businessId
                && price.ProductId == product.ProductId
                && price.IsActive
                && price.ValidFrom <= now
                && (price.ValidUntil == null || price.ValidUntil > now)));
        if (!includeInactive)
            products = products.Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(category))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(category))
            {
                var searchTerm = term;
                products = products.Where(product => product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(query))
            {
                var searchTerm = term;
                products = products.Where(product => product.Name.Contains(searchTerm)
                    || product.Sku != null && product.Sku.Contains(searchTerm)
                    || product.Description != null && product.Description.Contains(searchTerm)
                    || product.CategoryName != null && product.CategoryName.Contains(searchTerm));
            }
        }
        var totalCount = await products.CountAsync(ct);
        var items = await products.OrderBy(product => product.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await ApplyPublishedPricesAsync(items, businessId, ct);
        return (items.Where(product => product.HasPublishedPrice).ToList(), totalCount);
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
        var query = _context.Products.AsNoTracking().Where(product => product.BusinessId == businessId);
        if (!includeInactive)
            query = query.Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            foreach (var term in CatalogSearchText.GetSearchTerms(search))
            {
                var searchTerm = term;
                query = query.Where(product => EF.Functions.Collate(product.Name, "Latin1_General_100_CI_AI").Contains(searchTerm)
                    || product.Sku != null && EF.Functions.Collate(product.Sku, "Latin1_General_100_CI_AI").Contains(searchTerm)
                    || product.Description != null && EF.Functions.Collate(product.Description, "Latin1_General_100_CI_AI").Contains(searchTerm)
                    || product.CategoryName != null && EF.Functions.Collate(product.CategoryName, "Latin1_General_100_CI_AI").Contains(searchTerm));
            }
        }
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(product => product.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await ApplyPublishedPricesAsync(items, businessId, ct);
        await ApplyInventoryBalancesAsync(items, businessId, ct);
        return (items, totalCount);
    }

    public async Task<Product?> GetByIdAsync(Guid businessId, Guid productId, CancellationToken ct = default)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.BusinessId == businessId && product.ProductId == productId, ct);
        if (product is not null)
        {
            await ApplyPublishedPricesAsync([product], businessId, ct);
            await ApplyInventoryBalancesAsync([product], businessId, ct);
        }
        return product;
    }

    public async Task<Product?> GetByExternalIdAsync(Guid businessId, Guid integrationConnectionId, string externalProductId, CancellationToken ct = default)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(product =>
            product.BusinessId == businessId && product.IntegrationConnectionId == integrationConnectionId && product.ExternalProductId == externalProductId, ct);
        if (product is not null)
            await ApplyPublishedPricesAsync([product], businessId, ct);
        return product;
    }

    public Task<Product> CreateAsync(Product product, CancellationToken ct = default)
    {
        var amount = product.UnitPrice;
        var currency = product.Currency;
        product.UnitPrice = 0m;
        product.Currency = "COP";
        _context.Products.Add(product);
        AddInitialPublishedPrice(product, amount, currency, DateTimeOffset.UtcNow);
        return Task.FromResult(product);
    }

    public async Task UpdateCategoryNameAsync(
        Guid businessId,
        Guid productCategoryId,
        string categoryName,
        CancellationToken ct = default)
    {
        var products = await _context.Products
            .Where(product => product.BusinessId == businessId
                && product.ProductCategoryId == productCategoryId)
            .ToListAsync(ct);
        foreach (var product in products)
        {
            product.CategoryName = categoryName;
            product.UpdatedAt = DateTime.UtcNow;
        }
    }
    public async Task<Product> UpdateAsync(Product product, CancellationToken ct = default)
    {
        await ReplacePublishedPriceIfChangedAsync(product, DateTimeOffset.UtcNow, ct);
        _context.Products.Update(product);
        _context.Entry(product).Property(item => item.UnitPrice).IsModified = false;
        _context.Entry(product).Property(item => item.Currency).IsModified = false;
        return product;
    }
}
