using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Pricing;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class LinkedPricePublicationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Linked_cost_prepares_the_child_price_with_its_margin_without_publishing_it()
    {
        fixture.DrainSynchronizationMessages();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await SeedProductAsync(rootId, 4_000m);
        await SeedProductAsync(childId, 1_000m);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.InventoryBalances
                  (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                   InventoryValue,LastProcessingSequence,UpdatedAt)
                VALUES(@BusinessId,@WarehouseId,@ChildId,1,800,800,1,SYSDATETIMEOFFSET());
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@ChildId", childId);
            await command.ExecuteNonQueryAsync();
        }
        using (var catalog = fixture.CreateAdminClient(
                   CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
                   CatalogPermissionCodes.ManagePrices, CatalogPermissionCodes.ManageCosts))
        {
            var configuration = (await catalog.GetFromJsonAsync<ProductMerchandisingConfiguration>(
                $"/api/commerce/v1/products/{rootId:D}/merchandising"))!;
            var request = new SaveProductMerchandisingRequest(
                configuration.ProductCategoryId, configuration.ProductBrandId,
                configuration.BaseUnitCode, configuration.ManageInventory,
                configuration.AllowsFractionalSale, configuration.IsWeighable,
                configuration.Scale, configuration.Barcodes, null,
                [new LinkedProductInput(childId, true, 2m, true, 2m)]);
            using var blocked = await catalog.PutAsJsonAsync(
                $"/api/commerce/v1/products/{rootId:D}/merchandising", request);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Contains("inventario en cero", await blocked.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var clear = connection.CreateCommand();
            clear.CommandText = "UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0 WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ChildId;";
            clear.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            clear.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            clear.Parameters.AddWithValue("@ChildId", childId);
            await clear.ExecuteNonQueryAsync();

            using var saved = await catalog.PutAsJsonAsync(
                $"/api/commerce/v1/products/{rootId:D}/merchandising", request);
            saved.EnsureSuccessStatusCode();
        }
        Assert.Equal(6_400m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(1_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        using (var inventory = fixture.CreateAdminClient(InventoryPermissionCodes.Read))
        {
            var childResults = await inventory.GetFromJsonAsync<InventoryProductPage>(
                $"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=LINK-{childId:N}&page=1&pageSize=20");
            Assert.NotNull(childResults);
            Assert.Empty(childResults!.Items);

            var rootResults = await inventory.GetFromJsonAsync<InventoryProductPage>(
                $"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=LINK-{rootId:N}&page=1&pageSize=20");
            Assert.NotNull(rootResults);
            Assert.Contains(rootResults!.Items, product => product.ProductId == rootId);
        }


        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices,
            PricingPermissionCodes.PublishPrices);

        using (var prepare = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{rootId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 20m, null, 1m,
                       PricingRoundingModes.Nearest, 4_000m)))
            prepare.EnsureSuccessStatusCode();

        Assert.Equal(1_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));

        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        var candidate = Assert.Single(candidates!.Items.Where(x => x.ProductId == rootId));
        Assert.Equal(5_000m, candidate.SuggestedSalePrice);

        using var publish = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                candidate.ProposalId, PriceInputModes.Margin, 20m, null, 1m,
                PricingRoundingModes.Nearest, candidate.ConcurrencyToken)]));
        publish.EnsureSuccessStatusCode();

        Assert.Equal(1_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND PublicationOrigin=N'LinkedProduct'", childId));

        var childCandidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        var childCandidate = Assert.Single(childCandidates!.Items.Where(x => x.ProductId == childId));
        Assert.Equal(10_000m, childCandidate.SuggestedSalePrice);
    }

    private async Task SeedProductAsync(Guid productId, decimal price)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @TaxProfileId UNIQUEIDENTIFIER=NEWID();
            INSERT dbo.TaxProfiles
              (TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES
              (@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products
              (ProductId,TenantId,BusinessId,ProductCode,Reference,Sku,Name,Description,
               BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,
               Currency,CreatedAt)
            VALUES
              (@ProductId,@TenantId,@BusinessId,@ProductCode,@ProductCode,@ProductCode,
               N'Producto vinculado',N'Prueba de publicacion vinculada',N'EA',
               @TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
               CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
               InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@Price,@Price,N'COP',N'Manual',@Cost,20,20,
               N'Margin',1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@ProductCode", $"LINK-{productId:N}");
        command.Parameters.AddWithValue("@TaxCode", $"TL-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@Price", price);
        command.Parameters.AddWithValue("@Cost", price * 0.8m);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Product", productId);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected SQL scalar was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
