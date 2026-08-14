using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
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
            .Select(price => new { price.ProductId, price.Amount, price.CurrencyCode })
            .ToDictionaryAsync(price => price.ProductId, ct);

        foreach (var product in products)
        {
            if (prices.TryGetValue(product.ProductId, out var price))
            {
                product.UnitPrice = price.Amount;
                product.Currency = price.CurrencyCode;
                product.HasPublishedPrice = true;
                continue;
            }

            product.UnitPrice = 0m;
            product.Currency = "COP";
            product.HasPublishedPrice = false;
        }
    }
    private void AddInitialPublishedPrice(Product product, decimal amount, string currency, DateTimeOffset now)
    {
        if (amount <= 0m)
            return;

        _context.PublishedProductPrices.Add(new PublishedProductPriceRow
        {
            ProductPriceId = Guid.NewGuid(),
            BusinessId = product.BusinessId,
            ProductId = product.ProductId,
            Amount = amount,
            CurrencyCode = NormalizeCurrency(currency),
            ValidFrom = now,
            IsActive = true,
            CreatedAt = now
        });
    }

    private async Task ReplacePublishedPriceIfChangedAsync(Product product, DateTimeOffset now, CancellationToken ct)
    {
        var current = await _context.PublishedProductPrices
            .Where(price => price.BusinessId == product.BusinessId
                && price.ProductId == product.ProductId
                && price.IsActive)
            .SingleOrDefaultAsync(ct);
        var amount = product.UnitPrice;
        var currency = NormalizeCurrency(product.Currency);

        if (current is null)
        {
            AddInitialPublishedPrice(product, amount, currency, now);
            return;
        }

        if (current.Amount == amount && string.Equals(current.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase))
            return;

        current.IsActive = false;
        current.ValidUntil = now;
        AddInitialPublishedPrice(product, amount, currency, now);
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "COP" : currency.Trim().ToUpperInvariant()[..Math.Min(3, currency.Trim().Length)];
}