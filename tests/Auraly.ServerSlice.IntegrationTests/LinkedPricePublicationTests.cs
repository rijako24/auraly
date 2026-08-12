using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class LinkedPricePublicationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Linked_prices_change_only_when_the_root_price_is_published()
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
                INSERT dbo.ProductLinks
                  (ProductLinkId,BusinessId,ChildProductId,ParentProductId,
                   InventoryFactor,PriceFactor,SharesInventory,SharesPrice,IsActive,CreatedAt)
                VALUES
                  (NEWID(),@BusinessId,@ChildId,@RootId,2,2,1,1,1,SYSDATETIMEOFFSET());
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@ChildId", childId);
            command.Parameters.AddWithValue("@RootId", rootId);
            await command.ExecuteNonQueryAsync();
        }
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

        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND PublicationOrigin=N'LinkedProduct'", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.CatalogChanges WHERE ProductId=@Product AND ChangeKind=N'Upsert'", childId));
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
              (ProductId,BusinessId,ProductCode,Reference,Sku,Name,Description,
               BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,
               UnitPrice,Currency,CreatedAt)
            VALUES
              (@ProductId,@BusinessId,@ProductCode,@ProductCode,@ProductCode,
               N'Producto vinculado',N'Prueba de publicacion vinculada',N'EA',
               @TaxProfileId,1,0,1,0,@Price,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@Price,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@ProductCode", $"LINK-{productId:N}");
        command.Parameters.AddWithValue("@TaxCode", $"TL-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@Price", price);
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
