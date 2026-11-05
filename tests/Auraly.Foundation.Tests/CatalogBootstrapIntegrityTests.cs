using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;

namespace Auraly.Foundation.Tests;

public sealed class CatalogBootstrapIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bootstrap_json_preserves_the_full_product_contract_and_presentation(bool webDefaults)
    {
        var item = Item(Guid.NewGuid(), 12m) with
        {
            IsWeighable = true,
            AllowsFractionalSale = true,
            HasIngredients = true,
            Presentation = new("https://example.test/product.png", Guid.NewGuid(),
                Guid.NewGuid(), "Azul", Guid.NewGuid(), "M")
        };
        var page = new CatalogBootstrapPage(Guid.NewGuid(), 42, null, false,
            CatalogBootstrapIntegrity.Compute([item]), [item]);
        var options = new JsonSerializerOptions(webDefaults
            ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);

        var received = JsonSerializer.Deserialize<CatalogBootstrapPage>(
            JsonSerializer.Serialize(page, options), options);

        Assert.NotNull(received);
        var product = Assert.Single(received.Items);
        Assert.Equal(JsonSerializer.Serialize(item, options), JsonSerializer.Serialize(product, options));
        Assert.Equal(item.UnitPrice, product.UnitPrice);
        Assert.Equal(item.UnitCost, product.UnitCost);
        Assert.Equal(item.TaxRate, product.TaxRate);
        Assert.True(product.IsWeighable);
        Assert.True(product.AllowsFractionalSale);
        Assert.True(product.HasIngredients);
        Assert.Equal(item.InventoryProductId, product.InventoryProductId);
        Assert.Equal(12m, product.InventoryFactor);
        Assert.Equal(item.Presentation, product.Presentation);
        Assert.True(CatalogBootstrapIntegrity.IsValid(received.Items, received.IntegrityHash));
    }

    [Fact]
    public void Legacy_catalog_json_without_presentation_keeps_its_product_defaults()
    {
        const string json = """
            {"productId":"10000000-0000-0000-0000-000000000001","productCode":"OLD-1",
             "name":"Producto existente","baseUnitCode":"UND","taxCode":"01","taxRate":19,
             "unitPrice":25000,"currencyCode":"COP","isActive":true,"barcodes":["770000000001"],
             "identifiers":[]}
            """;

        var product = JsonSerializer.Deserialize<PosCatalogItem>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(product);
        Assert.Equal("OLD-1", product.ProductCode);
        Assert.Equal(25_000m, product.UnitPrice);
        Assert.True(product.ManagesStock);
        Assert.Equal(1m, product.InventoryFactor);
        Assert.Null(product.Presentation);
        Assert.Null(product.InventoryProductId);
        Assert.False(product.IsWeighable);
        Assert.False(product.AllowsFractionalSale);
        Assert.False(product.HasIngredients);
        Assert.False(product.IsCombo);
    }

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
        Assert.True(CatalogBootstrapIntegrity.IsValid([item with { HasIngredients = true }], legacyDigest));
        Assert.True(CatalogBootstrapIntegrity.IsValid([item with { IsCombo = true }], legacyDigest));
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
