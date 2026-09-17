using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class LinkedPricePublicationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Cost_link_parent_and_child_are_listed_and_published_independently()
    {
        fixture.DrainSynchronizationMessages();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await SeedProductAsync(parentId, 4_000m, "Producto principal");
        await SeedProductAsync(childId, 1_000m, "Producto hijo");
        await LinkCostAsync(parentId, childId, 2m);

        using var pricing = PricingClient();
        using (var prepareParent = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{parentId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.SalePrice, null, 5_100m, 1m,
                       PricingRoundingModes.Nearest, 4_000m)))
            Assert.True(prepareParent.IsSuccessStatusCode,
                await prepareParent.Content.ReadAsStringAsync());

        var childContext = await pricing.GetFromJsonAsync<ProductPricingContext>(
            $"/api/commerce/v1/pricing/products/{childId:D}/context");
        Assert.NotNull(childContext);
        Assert.True(childContext!.IsCostLinked);
        Assert.Equal(parentId, childContext.CostSourceProductId);
        Assert.Equal("Producto principal", childContext.CostSourceProductName);
        Assert.Equal(2m, childContext.CostFactor);
        Assert.Equal(8_000m, childContext.CostBasisAmount);

        using (var forbiddenCostChange = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{childId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, 7_000m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, forbiddenCostChange.StatusCode);
            Assert.Contains("no se puede editar", await forbiddenCostChange.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
        }

        using (var prepareChild = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{childId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, null)))
            prepareChild.EnsureSuccessStatusCode();

        var candidates = await PendingAsync(pricing);
        var parent = Assert.Single(candidates.Items.Where(item => item.ProductId == parentId));
        var child = Assert.Single(candidates.Items.Where(item => item.ProductId == childId));

        using (var publishChild = await PublishAsync(pricing, child))
            publishChild.EnsureSuccessStatusCode();

        Assert.Equal(10_667m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(4_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", parentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", parentId));

        var childVersionsAfterOwnPublication = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId);
        using (var publishParent = await PublishAsync(pricing, parent))
            publishParent.EnsureSuccessStatusCode();

        Assert.Equal(5_100m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", parentId));
        Assert.Equal(10_667m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", childId));
        Assert.Equal(childVersionsAfterOwnPublication, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", childId));

        var childHistory = await pricing.GetFromJsonAsync<ProductPriceHistoryPage>(
            $"/api/commerce/v1/pricing/products/{childId:D}/history");
        var childPublication = Assert.Single(childHistory!.Items.Where(item => item.ActivityType == "Publication"));
        Assert.Equal(8_000m, childPublication.CostBasisAmount);
        Assert.Equal(25.002344m, childPublication.EffectiveMarginPercent);
        Assert.Equal(10_667m, childPublication.PreparedAmount);
    }

    [Fact]
    public async Task Parent_cost_change_reprepares_child_with_its_own_margin()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await SeedProductAsync(parentId, 4_000m, "Principal con cambios");
        await SeedProductAsync(childId, 1_000m, "Hijo con margen propio");
        await LinkCostAsync(parentId, childId, 2m);

        using var pricing = PricingClient();
        using (var firstParentCost = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{parentId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 20m, null, 1m,
                       PricingRoundingModes.Nearest, 4_000m)))
            firstParentCost.EnsureSuccessStatusCode();
        using (var childMargin = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{childId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 25m, null, 1m,
                       PricingRoundingModes.Nearest, null)))
            childMargin.EnsureSuccessStatusCode();
        using (var secondParentCost = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{parentId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 20m, null, 1m,
                       PricingRoundingModes.Nearest, 5_000m)))
            secondParentCost.EnsureSuccessStatusCode();

        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
        Assert.Equal(13_333m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
        Assert.Equal(25m, await ScalarAsync<decimal>(
            "SELECT TargetMarginPercent FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", childId));
    }

    [Fact]
    public async Task Publish_all_pending_processes_six_thousand_products_in_one_request()
    {
        const int batchSize = 6_000;
        var marker = $"BATCH-{Guid.NewGuid():N}"[..18];
        var productIds = Enumerable.Range(0, batchSize).Select(_ => Guid.NewGuid()).ToArray();
        var taxProfileId = await SeedPreparedBatchAsync(productIds, marker);

        try
        {
            using var pricing = PricingClient();
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var publication = await pricing.PostAsJsonAsync(
                "/api/commerce/v1/pricing/publish-pending",
                new PublishPendingPricesRequest(marker, null, null));
            started.Stop();
            Assert.True(publication.IsSuccessStatusCode,
                $"La publicación respondió {(int)publication.StatusCode} después de {started.Elapsed}: " +
                await publication.Content.ReadAsStringAsync());
            var result = await publication.Content.ReadFromJsonAsync<PublishPricesResult>();
            Assert.NotNull(result);
            Assert.Equal(batchSize, result!.Items.Count);
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(30),
                $"La publicación de {batchSize} productos tomó {started.Elapsed}.");
        }
        finally
        {
            await DeletePreparedBatchAsync(productIds, taxProfileId);
        }
    }

    private HttpClient PricingClient() => fixture.CreateAdminClient(
        PricingPermissionCodes.Read,
        PricingPermissionCodes.ReadCostBasis,
        PricingPermissionCodes.PreparePrices,
        PricingPermissionCodes.PublishPrices,
        PricingPermissionCodes.BulkPublish,
        PricingPermissionCodes.ReadHistory);

    private static Task<HttpResponseMessage> PublishAsync(
        HttpClient client, PriceRevisionListItem item) =>
        client.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                item.ProposalId,PriceInputModes.Margin,
                item.TargetMarginPercent,null,1m,PricingRoundingModes.Nearest,
                item.ConcurrencyToken)]));

    private static async Task<PriceRevisionPage> PendingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved"))!;

    private async Task LinkCostAsync(Guid parentId, Guid childId, decimal factor)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.ProductLinks
              (ProductLinkId,BusinessId,ChildProductId,ParentProductId,
               InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,
               SharesPrice,AllowsConversion,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ChildId,@ParentId,NULL,@Factor,NULL,0,1,0,1,
                   SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ParentId", parentId);
        command.Parameters.AddWithValue("@ChildId", childId);
        command.Parameters.AddWithValue("@Factor", factor);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedProductAsync(Guid productId, decimal price, string name)
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
               @Name,N'Prueba de publicacion vinculada',N'EA',
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
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Price", price);
        command.Parameters.AddWithValue("@Cost", price * 0.8m);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedPreparedBatchAsync(Guid[] productIds, string marker)
    {
        var taxProfileId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 180;
        command.CommandText = """
            DECLARE @Products TABLE(Ordinal INT NOT NULL,ProductId UNIQUEIDENTIFIER PRIMARY KEY);
            INSERT @Products
            SELECT CONVERT(INT,[key]),CONVERT(UNIQUEIDENTIFIER,value)
            FROM OPENJSON(@ProductIds);

            INSERT dbo.TaxProfiles
              (TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@Marker,N'Sin impuesto masivo',0,1,SYSDATETIMEOFFSET());

            INSERT dbo.Products
              (ProductId,TenantId,BusinessId,ProductCode,Reference,Sku,Name,Description,
               BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,
               Currency,CreatedAt)
            SELECT ProductId,@TenantId,@BusinessId,
                   CONCAT(@Marker,N'-',Ordinal),CONCAT(@Marker,N'-',Ordinal),
                   CONCAT(@Marker,N'-',Ordinal),CONCAT(@Marker,N' Producto ',Ordinal),
                   N'Prueba de publicación masiva',N'EA',@TaxProfileId,1,0,1,0,N'COP',
                   SYSDATETIMEOFFSET()
            FROM @Products;

            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
               CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
               InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),@BusinessId,ProductId,1000,1000,N'COP',N'Manual',800,20,20,
                   N'Margin',1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM @Products;

            INSERT dbo.ProductPricePreparations
              (ProductPricePreparationId,BusinessId,ProductId,PreparationOrigin,
               PublicAmountSnapshot,PreparedAmount,CostBasisType,CostBasisAmount,
               TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
               RoundingMode,Status,PreparedAt)
            SELECT NEWID(),@BusinessId,ProductId,N'Product',1000,1250,N'Manual',1000,
                   20,20,N'Margin',1,N'Nearest',N'Pending',SYSDATETIMEOFFSET()
            FROM @Products;
            """;
        command.Parameters.Add("@ProductIds", System.Data.SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(productIds);
        command.Parameters.AddWithValue("@TaxProfileId", taxProfileId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Marker", marker);
        await command.ExecuteNonQueryAsync();
        return taxProfileId;
    }

    private async Task DeletePreparedBatchAsync(Guid[] productIds, Guid taxProfileId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 180;
        command.CommandText = """
            CREATE TABLE #Products(ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
            INSERT #Products SELECT CONVERT(UNIQUEIDENTIFIER,value) FROM OPENJSON(@ProductIds);
            DELETE message
            FROM dbo.PosSynchronizationOutboxMessages message
            INNER JOIN dbo.CatalogChanges change
              ON change.CatalogChangeId=message.AvailableThroughCursor
            INNER JOIN #Products product ON product.ProductId=change.ProductId;
            DELETE audit FROM dbo.PricePublicationAudits audit
              INNER JOIN #Products product ON product.ProductId=audit.ProductId;
            DELETE preparation FROM dbo.ProductPricePreparations preparation
              INNER JOIN #Products product ON product.ProductId=preparation.ProductId;
            DELETE price FROM dbo.ProductPrices price
              INNER JOIN #Products product ON product.ProductId=price.ProductId;
            DELETE change FROM dbo.CatalogChanges change
              INNER JOIN #Products product ON product.ProductId=change.ProductId;
            DELETE value FROM dbo.Products value
              INNER JOIN #Products product ON product.ProductId=value.ProductId;
            DELETE dbo.TaxProfiles WHERE TaxProfileId=@TaxProfileId;
            """;
        command.Parameters.Add("@ProductIds", System.Data.SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(productIds);
        command.Parameters.AddWithValue("@TaxProfileId", taxProfileId);
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
