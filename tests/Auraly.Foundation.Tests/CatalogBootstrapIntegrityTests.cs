using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_product_message_rejects_omission_of_any_field(bool webDefaults)
    {
        var options = new JsonSerializerOptions(webDefaults
            ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
        var product = JsonSerializer.SerializeToNode(Item(Guid.NewGuid(), 12m), options)!.AsObject();
        Assert.Equal(31, product.Count);
        foreach (var field in product.Select(property => property.Key).ToArray())
        {
            var incomplete = (JsonObject)product.DeepClone();
            Assert.True(incomplete.Remove(field));
            var exception = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<PosCatalogItem>(incomplete.ToJsonString(), options));
            Assert.Contains(field, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Integrity_protects_inventory_composition_and_presentation_as_well_as_financial_values()
    {
        var item = Item(Guid.NewGuid(), 12m);
        var digest = CatalogBootstrapIntegrity.Compute([item]);
        var changes = new[]
        {
            item with { InventoryProductId = Guid.NewGuid() },
            item with { InventoryFactor = 6m },
            item with { IsGenericProduct = true },
            item with { IsIngredient = true },
            item with { HasIngredients = true },
            item with { IsCombo = true },
            item with { Presentation = new("https://example.test/product.png", Guid.NewGuid(), null, null, null, null) },
            item with { UnitCost = item.UnitCost + 1m },
            item with { UnitPrice = item.UnitPrice + 1m }
        };
        Assert.True(CatalogBootstrapIntegrity.IsValid([item], digest));
        Assert.All(changes, changed => Assert.False(CatalogBootstrapIntegrity.IsValid([changed], digest)));
    }

    [Fact]
    public void Validation_rejects_a_changed_product_or_invalid_digest()
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
