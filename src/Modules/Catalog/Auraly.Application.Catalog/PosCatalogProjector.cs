using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;

namespace Auraly.Application.Catalog;

public sealed class PosCatalogProjector
{
    public PosCatalogProduct Project(Product product, string taxCode, decimal taxRate)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (string.IsNullOrWhiteSpace(taxCode)) throw new ArgumentException("A tax code is required.", nameof(taxCode));
        if (taxRate < 0) throw new ArgumentOutOfRangeException(nameof(taxRate));

        return new PosCatalogProduct(
            product.Id,
            product.ProductCode,
            product.Name,
            product.Barcodes.Order(StringComparer.Ordinal).ToArray(),
            product.IsActive,
            product.IsWeighed,
            taxCode.Trim(),
            taxRate);
    }
}
