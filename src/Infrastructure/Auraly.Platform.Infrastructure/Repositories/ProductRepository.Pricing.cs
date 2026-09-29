using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data.ReadModels;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    private async Task ApplyPublishedPricesAsync(
        IReadOnlyCollection<Product> products,
        Guid businessId,
        CancellationToken ct)
    {
        if (products.Count == 0)
            return;

        var productIds = products.Select(product => product.ProductId).Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;
        var prices = await _context.PublishedProductPrices
            .AsNoTracking()
            .Where(price => price.BusinessId == businessId
                && productIds.Contains(price.ProductId)
                && price.IsActive
                && price.ValidFrom <= now
                && (price.ValidUntil == null || price.ValidUntil > now))
            .Select(price => new
            {
                price.ProductId,
                price.Amount,
                price.CurrencyCode,
                UnitCost = price.CostBasisAmount ?? 0m
            })
            .ToDictionaryAsync(price => price.ProductId, ct);

        foreach (var product in products)
        {
            product.BusinessId = businessId;
            if (prices.TryGetValue(product.ProductId, out var price))
            {
                product.UnitPrice = price.Amount;
                product.UnitCost = price.UnitCost;
                product.Currency = price.CurrencyCode;
                product.HasPublishedPrice = true;
                continue;
            }

            product.UnitPrice = 0m;
            product.UnitCost = 0m;
            product.Currency = "COP";
            product.HasPublishedPrice = false;
        }
    }
    private void AddInitialPublishedPrices(
        Product product,
        IReadOnlyCollection<Guid> businessIds,
        decimal amount,
        string currency,
        DateTimeOffset now)
    {
        if (amount <= 0m)
            return;

        _context.PublishedProductPrices.AddRange(businessIds.Select(businessId => new PublishedProductPriceRow
        {
            ProductPriceId = Guid.NewGuid(),
            BusinessId = businessId,
            ProductId = product.ProductId,
            Amount = amount,
            CurrencyCode = NormalizeCurrency(currency),
            ValidFrom = now,
            IsActive = true,
            CreatedAt = now
        }));
    }

    public Task PublishPriceAsync(
        Product product, decimal amount, string currency, CancellationToken ct = default) =>
        PublishPricesAsync([new ProductPricePublication(product, amount, currency)], ct);

    public async Task PublishPricesAsync(
        IReadOnlyCollection<ProductPricePublication> publications, CancellationToken ct = default)
    {
        if (publications.Count == 0) return;
        var products = publications.Select(publication => publication.Product).ToArray();
        var productIds = products.Select(product => product.ProductId).Distinct().ToArray();
        var source = products[0];
        if (productIds.Length != products.Length || products.Any(product =>
                product.TenantId != source.TenantId || product.BusinessId != source.BusinessId))
            throw new InvalidOperationException("A product price publication batch must contain unique products from one business.");
        var businessIds = await _context.Businesses.AsNoTracking()
            .Where(target => target.TenantId == source.TenantId && target.IsActive
                && (target.BusinessId == source.BusinessId ||
                    target.SharesProductPrices && _context.Businesses.Any(current =>
                        current.BusinessId == source.BusinessId && current.TenantId == source.TenantId
                        && current.SharesProductPrices && current.IsActive)))
            .Select(target => target.BusinessId)
            .ToArrayAsync(ct);
        if (businessIds.Length == 0 || await _context.Products.CountAsync(
                item => productIds.Contains(item.ProductId) && item.TenantId == source.TenantId, ct) != productIds.Length)
            throw new InvalidOperationException("The product price scope is invalid.");

        var currentPrices = await _context.PublishedProductPrices
            .Where(price => productIds.Contains(price.ProductId) && price.IsActive
                && businessIds.Contains(price.BusinessId))
            .ToDictionaryAsync(price => new { price.ProductId, price.BusinessId }, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var publication in publications)
        {
            var normalizedCurrency = NormalizeCurrency(publication.Currency);
            foreach (var businessId in businessIds)
            {
                currentPrices.TryGetValue(new { publication.Product.ProductId, BusinessId = businessId }, out var current);
                if (current is not null && current.Amount == publication.Amount &&
                    string.Equals(current.CurrencyCode, normalizedCurrency, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (current is not null)
                {
                    current.IsActive = false;
                    current.ValidUntil = now;
                }
                AddInitialPublishedPrices(publication.Product, [businessId], publication.Amount, normalizedCurrency, now);
            }
        }
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "COP" : currency.Trim().ToUpperInvariant()[..Math.Min(3, currency.Trim().Length)];
}
