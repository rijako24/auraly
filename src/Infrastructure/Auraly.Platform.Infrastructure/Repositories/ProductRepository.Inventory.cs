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

        var rows = await _context.Database.SqlQuery<InventoryQuantityProjection>($"""
            SELECT ProductId,SUM(QuantityOnHand) AS QuantityOnHand
            FROM dbo.InventoryBalances
            WHERE BusinessId={businessId}
            GROUP BY ProductId
            """).ToListAsync(ct);
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
