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
    public async Task Inventory_and_conversion_links_remain_independent_price_publications()
    {
        var rootId = Guid.NewGuid();
        var inventoryChildId = Guid.NewGuid();
        var conversionChildId = Guid.NewGuid();
        await SeedProductAsync(rootId, 4_000m);
        await SeedProductAsync(inventoryChildId, 2_000m);
        await SeedProductAsync(conversionChildId, 1_000m);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var link = connection.CreateCommand();
            link.CommandText = """
                INSERT dbo.ProductLinks
                  (ProductLinkId,BusinessId,ChildProductId,ParentProductId,
                   InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,
                   SharesPrice,AllowsConversion,IsActive,CreatedAt)
                VALUES
                  (NEWID(),@BusinessId,@InventoryChildId,@RootId,2,NULL,NULL,1,0,0,1,SYSDATETIMEOFFSET()),
                  (NEWID(),@BusinessId,@ConversionChildId,@RootId,NULL,NULL,3,0,0,1,1,SYSDATETIMEOFFSET());
                """;
            link.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            link.Parameters.AddWithValue("@RootId", rootId);
            link.Parameters.AddWithValue("@InventoryChildId", inventoryChildId);
            link.Parameters.AddWithValue("@ConversionChildId", conversionChildId);
            await link.ExecuteNonQueryAsync();
        }

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices);
        foreach (var productId in new[] { rootId, inventoryChildId, conversionChildId })
        {
            using var preparation = await pricing.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
                new PublishProductPriceRequest(
                    PriceInputModes.Margin, 20m, null, 1m,
                    PricingRoundingModes.Nearest, 800m));
            preparation.EnsureSuccessStatusCode();
        }

        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        Assert.NotNull(candidates);
        foreach (var productId in new[] { rootId, inventoryChildId, conversionChildId })
        {
            var candidate = Assert.Single(candidates!.Items.Where(item => item.ProductId == productId));
            Assert.Empty(candidate.LinkedProducts);
        }
    }

    [Fact]
    public async Task Price_link_children_are_informational_and_publish_with_the_parent()
    {
        fixture.DrainSynchronizationMessages();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await SeedProductAsync(rootId, 4_000m);
        await SeedProductAsync(childId, 1_000m);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var link = connection.CreateCommand();
            link.CommandText = """
                INSERT dbo.ProductLinks
                  (ProductLinkId,BusinessId,ChildProductId,ParentProductId,
                   InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,
                   SharesPrice,AllowsConversion,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,@ChildId,@RootId,2,2,NULL,1,1,0,1,
                       SYSDATETIMEOFFSET());
                """;
            link.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            link.Parameters.AddWithValue("@RootId", rootId);
            link.Parameters.AddWithValue("@ChildId", childId);
            await link.ExecuteNonQueryAsync();
        }

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices,
            PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.BulkPublish);
        using (var prepareRoot = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{rootId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.SalePrice, null, 5_100m, 1m,
                       PricingRoundingModes.Nearest, 4_000m)))
            prepareRoot.EnsureSuccessStatusCode();
        using (var prepareChild = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{childId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, 800m)))
            prepareChild.EnsureSuccessStatusCode();

        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        var selected = Assert.Single(candidates!.Items.Where(item => item.ProductId == rootId));
        Assert.DoesNotContain(candidates.Items, item => item.ProductId == childId);
        var linked = Assert.Single(selected.LinkedProducts);
        Assert.Equal(childId, linked.ProductId);
        Assert.Equal(2m, linked.PriceFactor);
        Assert.Equal(10_667m, linked.PreparedSalePrice);

        var childPreparationId = await ScalarAsync<Guid>(
            "SELECT ProductPricePreparationId FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId);
        var childConcurrencyToken = Convert.ToBase64String(await ScalarAsync<byte[]>(
            "SELECT RowVersion FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
        using (var childOnlyPublication = await pricing.PostAsJsonAsync(
                   "/api/commerce/v1/pricing/publish",
                   new PublishPricesRequest([new PublishPriceItem(
                       childPreparationId, PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, childConcurrencyToken)])))
        {
            Assert.Equal(System.Net.HttpStatusCode.Conflict, childOnlyPublication.StatusCode);
            Assert.Contains("producto principal", await childOnlyPublication.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
        }

        using var publish = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                selected.ProposalId,
                PriceInputModes.SalePrice,
                null,
                selected.SuggestedSalePrice,
                1m,
                PricingRoundingModes.Nearest,
                selected.ConcurrencyToken)]));
        publish.EnsureSuccessStatusCode();
        var publication = await publish.Content.ReadFromJsonAsync<PublishPricesResult>();
        Assert.NotNull(publication);
        Assert.Contains(publication!.Items, item => item.ProductId == rootId);
        Assert.Contains(publication.Items, item => item.ProductId == childId);

        Assert.Equal(5_100m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", rootId));
        Assert.Equal(10_667m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'LinkedProduct' AND Status=N'Published'", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND PublicationOrigin=N'LinkedProduct'", childId));
        Assert.True(await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.CatalogChanges WHERE ProductId=@Product AND ChangeKind=N'Upsert'", childId) > 0);
        Assert.True(await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages message INNER JOIN dbo.CatalogChanges change ON change.BusinessId=message.BusinessId AND change.CatalogChangeId=message.AvailableThroughCursor WHERE message.Stream=N'Catalog' AND change.ProductId=@Product", childId) > 0);
    }

    [Fact]
    public async Task Linked_child_keeps_its_margin_and_is_published_atomically_with_the_parent()
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
            "SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
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

        // A later manual preparation on the child owns its margin policy. Publishing the
        // parent applies that margin to the linked cost in the same transaction.
        using (var prepareChild = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{childId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, 800m)))
            prepareChild.EnsureSuccessStatusCode();

        Assert.Equal(1_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));

        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        var candidate = Assert.Single(candidates!.Items.Where(x => x.ProductId == rootId));
        Assert.Equal(5_000m, candidate.SuggestedSalePrice);
        Assert.DoesNotContain(candidates.Items, item => item.ProductId == childId);
        var linkedCandidate = Assert.Single(candidate.LinkedProducts);
        Assert.Equal(childId, linkedCandidate.ProductId);
        Assert.Equal(10_667m, linkedCandidate.PreparedSalePrice);

        using var publish = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                candidate.ProposalId, PriceInputModes.Margin, 20m, null, 1m,
                PricingRoundingModes.Nearest, candidate.ConcurrencyToken)]));
        publish.EnsureSuccessStatusCode();

        Assert.Equal(10_667m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'LinkedProduct' AND Status=N'Published'", childId));
        Assert.Equal(25m, await ScalarAsync<decimal>(
            "SELECT TargetMarginPercent FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'LinkedProduct' AND Status=N'Published'", childId));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND PublicationOrigin=N'LinkedProduct'", childId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
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
