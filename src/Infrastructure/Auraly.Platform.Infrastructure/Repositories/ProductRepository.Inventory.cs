using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    private async Task ApplyInventoryBalancesAsync(
        IReadOnlyCollection<Product> products,
        Guid businessId,
        CancellationToken ct)
    {
        if (products.Count == 0) return;

        var productIds = products.Select(product => product.ProductId).Distinct().ToArray();
        var rows = await (
            from balance in _context.InventoryBalances.AsNoTracking()
            join warehouse in _context.InventoryWarehouseScopes.AsNoTracking()
                on new { balance.BusinessId, balance.WarehouseId }
                equals new { warehouse.BusinessId, warehouse.WarehouseId }
            where balance.BusinessId == businessId
                && productIds.Contains(balance.ProductId)
                && warehouse.IsActive
                && !warehouse.IsSystem
            group balance by balance.ProductId
            into balances
            select new InventoryQuantityProjection
            {
                ProductId = balances.Key,
                QuantityOnHand = balances.Sum(balance => balance.QuantityOnHand)
            }).ToListAsync(ct);
        var quantities = rows.ToDictionary(row => row.ProductId, row => row.QuantityOnHand);

        foreach (var product in products)
            product.StockQuantity = product.ManageStock
                ? quantities.GetValueOrDefault(product.ProductId)
                : null;
    }

    private sealed class InventoryQuantityProjection
    {
        public Guid ProductId { get; set; }
        public decimal QuantityOnHand { get; set; }
    }
}
