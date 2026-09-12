using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;

namespace Auraly.Foundation.Tests;

public sealed class CatalogBootstrapIntegrityTests
{
    [Fact]
    public void Digest_remains_compatible_when_inventory_fields_are_added_to_the_contract()
    {
        var inventoryProductId = Guid.NewGuid();
        var item = Item(inventoryProductId, 12m);
        var legacyWireItem = new
        {
            item.ProductId,
            item.ProductCode,
            item.Reference,
            item.Name,
            item.BaseUnitCode,
            item.TaxCode,
            item.TaxRate,
            item.UnitPrice,
            item.CurrencyCode,
            item.IsActive,
            item.IsWeighable,
            item.AllowsFractionalSale,
            item.Scale,
            item.Barcodes,
            item.Identifiers,
            item.UnitCost,
            item.ManagesStock,
            item.CategoryName,
            item.ProductCategoryId,
            item.ProductBrandId,
            item.ProductCategoryAncestorIds,
            item.AverageUnitCost,
            item.LatestUnitCost,
            item.TargetMarginPercent,
        };
        var legacyDigest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { legacyWireItem }))))
            .ToLowerInvariant();

        Assert.Equal(legacyDigest, CatalogBootstrapIntegrity.Compute([item]));
        Assert.True(CatalogBootstrapIntegrity.IsValid([item], legacyDigest));
    }

    [Fact]
    public void Validation_rejects_a_change_to_the_stable_catalog_core()
    {
        var item = Item(Guid.NewGuid(), 1m);
        var digest = CatalogBootstrapIntegrity.Compute([item]);

        Assert.False(CatalogBootstrapIntegrity.IsValid(
            [item with { UnitPrice = item.UnitPrice + 1m }], digest));
        Assert.False(CatalogBootstrapIntegrity.IsValid([item], "not-a-digest"));
    }

    private static PosCatalogItem Item(Guid inventoryProductId, decimal inventoryFactor) => new(
        Guid.NewGuid(), "P-001", "REF-1", "Producto", "UND", "01", 19m, 25_000m, "COP",
        true, false, false, null, ["770000000001"], [new ProductIdentifierInput("Sku", "SKU-1")],
        12_000m, true, "Categoría", Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()],
        11_500m, 12_000m, 52m, inventoryProductId, inventoryFactor);
}
