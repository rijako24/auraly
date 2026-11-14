using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Pricing;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class CatalogVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_a_child_from_its_parent_releases_live_cost_and_inventory_controls_after_reloading(bool completeProduct)
    {
        var (tax, _, _) = await ConfigureCatalogAsync();
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Create, CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Read, CatalogPermissionCodes.ManageCosts, CatalogPermissionCodes.ManagePrices,
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis);
        var parentRequest = ProductRequest(tax, [new ProductPriceInput(10_000m)], []) with
            { Name = $"Principal para desvincular {Guid.NewGuid():N}" };
        var childRequest = ProductRequest(tax, [new ProductPriceInput(20_000m)], []) with
            { Name = $"Hijo para desvincular {Guid.NewGuid():N}" };
        var parent = await Create(parentRequest);
        var child = await Create(childRequest);
        var parentPath = $"/api/commerce/v1/products/{parent.ProductId}";
        var childPath = $"/api/commerce/v1/products/{child.ProductId}";
        var parentConfiguration = (await client.GetFromJsonAsync<ProductMerchandisingConfiguration>(parentPath + "/merchandising"))!;
        var originalInventory = parentConfiguration.ManageInventory;
        await SaveParent([new(child.ProductId, true, 2m, true, 2m)]);
        var linked = (await client.GetFromJsonAsync<ProductMerchandisingConfiguration>(childPath + "/merchandising"))!;
        Assert.Equal(parent.ProductId, linked.Link!.ParentProductId);
        Assert.True(linked.Link.SharesInventory);
        var linkedCost = (await client.GetFromJsonAsync<ProductPricingContext>(
            $"/api/commerce/v1/pricing/products/{child.ProductId}/context"))!;
        Assert.True(linkedCost.IsCostLinked);
        Assert.Equal(parent.ProductId, linkedCost.CostSourceProductId);

        await SaveParent([]);
        // Fresh API reads represent reopening after saving/reloading, rather
        // than trusting either the command body or a recovered local draft.
        var unlinked = (await client.GetFromJsonAsync<ProductMerchandisingConfiguration>(childPath + "/merchandising"))!;
        Assert.Null(unlinked.Link);
        var reloadedParent = (await client.GetFromJsonAsync<ProductMerchandisingConfiguration>(parentPath + "/merchandising"))!;
        Assert.Empty(reloadedParent.LinkedProducts);
        Assert.Equal(originalInventory, reloadedParent.ManageInventory);
        var ownCost = (await client.GetFromJsonAsync<ProductPricingContext>(
            $"/api/commerce/v1/pricing/products/{child.ProductId}/context"))!;
        Assert.False(ownCost.IsCostLinked);
        Assert.Null(ownCost.CostSourceProductId);

        using var edited = await client.PutAsJsonAsync(childPath, childRequest with
        {
            ProductCode = child.ProductCode, ManageInventory = true,
            Prices = [new ProductPriceInput(20_000m, CostBasisAmount: 9_000m, TargetMarginPercent: 20m)]
        });
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        var independentlyManaged = (await client.GetFromJsonAsync<ProductMerchandisingConfiguration>(childPath + "/merchandising"))!;
        Assert.True(independentlyManaged.ManageInventory);
        Assert.Null(independentlyManaged.Link);

        async Task<ProductDetail> Create(SaveProductRequest request)
        {
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/products", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ProductDetail>())!;
        }

        async Task SaveParent(IReadOnlyCollection<LinkedProductInput> children)
        {
            using var response = completeProduct
                ? await client.PutAsJsonAsync(parentPath, parentRequest with
                    { ProductCode = parent.ProductCode, LinkedProducts = children })
                : await client.PutAsJsonAsync(parentPath + "/merchandising", new SaveProductMerchandisingRequest(
                    parentConfiguration.ProductCategoryId, parentConfiguration.ProductBrandId, parentConfiguration.BaseUnitCode,
                    parentConfiguration.ManageInventory, parentConfiguration.AllowsFractionalSale, parentConfiguration.IsWeighable,
                    parentConfiguration.Scale, parentConfiguration.Barcodes, null, children));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
