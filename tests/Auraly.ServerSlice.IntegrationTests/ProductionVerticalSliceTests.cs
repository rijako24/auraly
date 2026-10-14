using System.Net;
using System.Net.Http.Json;
using System.Data;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class ProductionVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Ingredient_with_recipe_can_be_produced_then_consumed_as_stocked_component()
    {
        var raw = Guid.NewGuid();
        var sauce = Guid.NewGuid();
        var meal = Guid.NewGuid();
        await SeedAsync(raw, sauce, meal);
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update, InventoryPermissionCodes.Read,
            InventoryPermissionCodes.ReadCosts, InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Produce);
        using (var recipe = await client.PutAsJsonAsync($"/api/commerce/v1/products/{sauce:D}/recipe",
            new PublishProductRecipeRequest(0, [new(raw, 2m)])))
            Assert.True(recipe.IsSuccessStatusCode, await recipe.Content.ReadAsStringAsync());
        var options = await client.GetFromJsonAsync<ProductionProductPage>(
            $"/api/commerce/v1/inventory/production-products?warehouseId={fixture.WarehouseId:D}&search=Carne&page=1&pageSize=20");
        Assert.Contains(options!.Items, item => item.ProductId == sauce);
        var preview = await client.GetFromJsonAsync<ProductionPreview>(
            $"/api/commerce/v1/inventory/production-products/{sauce:D}/preview?warehouseId={fixture.WarehouseId:D}&quantity=4");
        Assert.Equal(8m, Assert.Single(preview!.Ingredients).RequiredQuantity);
        using (var missing = await SendRawAsync(client, "/api/commerce/v1/production/confirm",
            new ConfirmProductionRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                DateTimeOffset.UtcNow, sauce, 1, 4m, null), $"missing-{Guid.NewGuid():N}"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        // Production rejects insufficient input before enqueueing a document.
        Assert.Equal(0m, await BalanceAsync(sauce));
        await SendAsync(client, "/api/commerce/v1/inventory-adjustments/confirm",
            new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId,
                fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null,
                [new(1, raw, 20m, 5m)]), $"raw-{Guid.NewGuid():N}");
        var production = new ConfirmProductionRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, DateTimeOffset.UtcNow, sauce, 1, 4m, null);
        await SendAsync(client, "/api/commerce/v1/production/confirm", production,
            $"sauce-{production.DocumentId:N}");
        Assert.Equal(12m, await BalanceAsync(raw));
        Assert.Equal(4m, await BalanceAsync(sauce));
        using (var recipe = await client.PutAsJsonAsync($"/api/commerce/v1/products/{meal:D}/recipe",
            new PublishProductRecipeRequest(0, [new(sauce, 2m)])))
            Assert.True(recipe.IsSuccessStatusCode, await recipe.Content.ReadAsStringAsync());
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var currentMode = new SqlCommand("SELECT PointOfSaleMode FROM dbo.Businesses WHERE BusinessId=@BusinessId", connection);
            currentMode.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            var previousMode = (string?)await currentMode.ExecuteScalarAsync();
            await using var mode = new SqlCommand("UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar' WHERE BusinessId=@BusinessId", connection);
            mode.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await mode.ExecuteNonQueryAsync();
            try
            {
                await using var demand = new SqlCommand("purchasing.RestaurantDemandInputsGet", connection)
                { CommandType = CommandType.StoredProcedure };
                demand.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                demand.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
                demand.Parameters.AddWithValue("@ProductIdsJson", System.Text.Json.JsonSerializer.Serialize(new[] { meal }));
                demand.Parameters.AddWithValue("@StartDate", DateTime.UtcNow.Date);
                var edges = new HashSet<(Guid Parent, Guid Component)>();
                await using var reader = await demand.ExecuteReaderAsync();
                while (await reader.ReadAsync()) edges.Add((reader.GetGuid(0), reader.GetGuid(1)));
                Assert.Contains((meal, sauce), edges);
                Assert.Contains((sauce, raw), edges);
            }
            finally
            {
                await using var restore = new SqlCommand("UPDATE dbo.Businesses SET PointOfSaleMode=@Mode WHERE BusinessId=@BusinessId", connection);
                restore.Parameters.AddWithValue("@Mode", previousMode ?? "DirectSale");
                restore.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await restore.ExecuteNonQueryAsync();
            }
        }
        var mealProduction = new ConfirmProductionRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, DateTimeOffset.UtcNow, meal, 1, 2m, null);
        await SendAsync(client, "/api/commerce/v1/production/confirm", mealProduction,
            $"meal-{mealProduction.DocumentId:N}");
        Assert.Equal(12m, await BalanceAsync(raw));
        Assert.Equal(0m, await BalanceAsync(sauce));
        Assert.Equal(2m, await BalanceAsync(meal));
    }

    [Fact]
    public async Task Publishing_an_ingredient_recipe_rejects_an_indirect_cycle()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var meal = Guid.NewGuid();
        await SeedAsync(first, second, meal);
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update);
        using (var recipe = await client.PutAsJsonAsync($"/api/commerce/v1/products/{first:D}/recipe",
            new PublishProductRecipeRequest(0, [new(second, 1m)])))
            Assert.True(recipe.IsSuccessStatusCode, await recipe.Content.ReadAsStringAsync());
        using var cycle = await client.PutAsJsonAsync($"/api/commerce/v1/products/{second:D}/recipe",
            new PublishProductRecipeRequest(0, [new(first, 1m)]));
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        using var unchanged = await client.GetAsync(
            $"/api/commerce/v1/products/{second:D}/recipe");
        Assert.Equal(HttpStatusCode.NotFound, unchanged.StatusCode);
    }

    [Fact]
    public async Task Product_and_zero_balance_recipe_are_saved_atomically()
    {
        var bread = Guid.NewGuid();
        var meat = Guid.NewGuid();
        await SeedAsync(bread, meat, Guid.NewGuid());
        await using (var taxConnection = new SqlConnection(fixture.ConnectionString))
        {
            await taxConnection.OpenAsync();
            await using var tax = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@Tax)
                  INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
                  VALUES(@Tax,@Tenant,N'VAT19',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
                """, taxConnection);
            tax.Parameters.AddWithValue("@Tax", fixture.TaxProfileId);
            tax.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            await tax.ExecuteNonQueryAsync();
        }
        using var client = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            CatalogPermissionCodes.ManagePrices, CatalogPermissionCodes.ReadCosts, CatalogPermissionCodes.ManageCosts);
        var name = $"Producto receta {Guid.NewGuid():N}";
        var request = new SaveProductRequest(fixture.BusinessId, "", null, name, null,
            "EA", fixture.TaxProfileId, false, false, [], [],
            [new ProductPriceInput(12_500m, "COP", 10_000m, 20m)],
            [new SupplierCostInput(fixture.SupplierId, "900999001", "Proveedor E2E", null, 10_000m)],
            null, fixture.TaxProfileId,
            RecipeIngredients: [new(bread, 2m)], RecipeExpectedVersion: 0);
        using var saved = await client.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.True(saved.StatusCode == HttpStatusCode.Created,
            $"Expected Created, received {saved.StatusCode}: {await saved.Content.ReadAsStringAsync()}");
        var product = (await saved.Content.ReadFromJsonAsync<ProductDetail>())!;
        Assert.Equal(0m, await BalanceAsync(bread));
        var recipe = await client.GetFromJsonAsync<ProductRecipeDetail>($"/api/commerce/v1/products/{product.ProductId:D}/recipe");
        Assert.Equal(bread, Assert.Single(recipe!.Ingredients).IngredientProductId);

        using (var updated = await client.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId:D}",
            request with { Name = name + " editado", RecipeExpectedVersion = 1,
                RecipeIngredients = [new(meat, 1m)] }))
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var editedRecipe = await client.GetFromJsonAsync<ProductRecipeDetail>($"/api/commerce/v1/products/{product.ProductId:D}/recipe");
        Assert.Equal(2, editedRecipe!.Version);
        Assert.Equal(meat, Assert.Single(editedRecipe.Ingredients).IngredientProductId);

        var invalidName = $"Producto fallido {Guid.NewGuid():N}";
        using var rejected = await client.PostAsJsonAsync("/api/commerce/v1/products",
            request with { Name = invalidName, RecipeIngredients = [new(Guid.NewGuid(), 1m)] });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.Products WHERE TenantId=@TenantId AND Name=@Name", connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Name", invalidName);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Recipe_production_moves_ingredients_and_finished_stock_once_and_preserves_version()
    {
        var bread = Guid.NewGuid();
        var meat = Guid.NewGuid();
        var burger = Guid.NewGuid();
        await SeedAsync(bread, meat, burger);
        using var client = fixture.CreateAdminClient(
            CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            InventoryPermissionCodes.Read, InventoryPermissionCodes.ReadCosts,
            InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Produce);

        var ingredientsWithoutStock = await client.GetFromJsonAsync<ProductRecipeIngredientPage>(
            "/api/commerce/v1/products/recipe-ingredients?search=Pan&page=1&pageSize=20");
        Assert.Contains(ingredientsWithoutStock!.Items, item => item.ProductId == bread);
        Assert.Equal(0m, await BalanceAsync(bread));
        Assert.Equal(0m, await BalanceAsync(meat));

        using (var publish = await client.PutAsJsonAsync($"/api/commerce/v1/products/{burger:D}/recipe",
            new PublishProductRecipeRequest(0, [new(bread, 2m), new(meat, .5m)])))
        {
            Assert.True(publish.IsSuccessStatusCode, await publish.Content.ReadAsStringAsync());
            Assert.Equal(1, (await publish.Content.ReadFromJsonAsync<ProductRecipeDetail>())!.Version);
        }

        var occurred = DateTimeOffset.UtcNow;
        await SendAsync(client, "/api/commerce/v1/inventory-adjustments/confirm",
            new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                occurred, "INITIAL_BALANCE", null, "Ingredientes para prueba",
                [new(1, bread, 20m, 5m), new(2, meat, 10m, 2m)]), $"seed-{Guid.NewGuid():N}");

        var available = await client.GetFromJsonAsync<ProductionProductPage>(
            $"/api/commerce/v1/inventory/production-products?warehouseId={fixture.WarehouseId:D}&search=Hamburguesa&page=1&pageSize=20");
        Assert.Contains(available!.Items, item => item.ProductId == burger && item.RecipeVersion == 1);
        var preview = await client.GetFromJsonAsync<ProductionPreview>(
            $"/api/commerce/v1/inventory/production-products/{burger:D}/preview?warehouseId={fixture.WarehouseId:D}&quantity=4");
        Assert.NotNull(preview);
        Assert.Equal(8m, preview.Ingredients.Single(line => line.ProductId == bread).RequiredQuantity);
        Assert.Equal(2m, preview.Ingredients.Single(line => line.ProductId == meat).RequiredQuantity);

        var documentId = Guid.NewGuid();
        var request = new ConfirmProductionRequest(documentId, fixture.BusinessId, fixture.WarehouseId,
            occurred.AddMinutes(1), burger, 1, 4m, "Lote inicial");
        var key = $"production-{documentId:N}";
        var first = await SendAsync(client, "/api/commerce/v1/production/confirm", request, key);
        Assert.False(first.IdempotentReplay);
        var replay = await SendAsync(client, "/api/commerce/v1/production/confirm", request, key);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(12m, await BalanceAsync(bread));
        Assert.Equal(8m, await BalanceAsync(meat));
        Assert.Equal(4m, await BalanceAsync(burger));
        Assert.Equal(3, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", documentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(DISTINCT RecipeVersionId) FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", documentId));
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", documentId));
        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT TotalValueChange FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", documentId));
        var detail = await client.GetFromJsonAsync<InventoryOperationDetail>(
            $"/api/commerce/v1/inventory/operations/{documentId:D}");
        Assert.NotNull(detail?.RecipeVersionId);
        Assert.Equal("Production", detail!.DocumentType);
        Assert.Equal(1, detail.RecipeVersion);

        using (var publish = await client.PutAsJsonAsync($"/api/commerce/v1/products/{burger:D}/recipe",
            new PublishProductRecipeRequest(1, [new(bread, 1m), new(meat, 1m)])))
            Assert.True(publish.IsSuccessStatusCode, await publish.Content.ReadAsStringAsync());
        using (var stale = await SendRawAsync(client, "/api/commerce/v1/production/confirm",
            request with { DocumentId = Guid.NewGuid() }, $"production-stale-{Guid.NewGuid():N}"))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(4m, await BalanceAsync(burger));

        using (var disable = await client.PutAsJsonAsync($"/api/commerce/v1/products/{burger:D}/recipe",
            new PublishProductRecipeRequest(2, [])))
        {
            Assert.True(disable.IsSuccessStatusCode, await disable.Content.ReadAsStringAsync());
            Assert.False((await disable.Content.ReadFromJsonAsync<ProductRecipeDetail>())!.IsEnabled);
        }
        var afterDisable = await client.GetFromJsonAsync<ProductionProductPage>(
            $"/api/commerce/v1/inventory/production-products?warehouseId={fixture.WarehouseId:D}&search=Hamburguesa&page=1&pageSize=20");
        Assert.DoesNotContain(afterDisable!.Items, item => item.ProductId == burger);
        using var disabledPreview = await client.GetAsync(
            $"/api/commerce/v1/inventory/production-products/{burger:D}/preview?warehouseId={fixture.WarehouseId:D}&quantity=4");
        Assert.Equal(HttpStatusCode.NotFound, disabledPreview.StatusCode);
    }

    private async Task SeedAsync(Guid bread, Guid meat, Guid burger)
    {
        const string sql = """
            DECLARE @TaxProfileId uniqueidentifier=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@TenantId,@TaxCode,N'IVA receta',19,1,SYSUTCDATETIME());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,TaxProfileId,Name,Currency,ManageStock,IsIngredient,IsActive,CreatedAt)
            VALUES(@Bread,@TenantId,@BreadCode,N'EA',@TaxProfileId,N'Pan de hamburguesa',N'COP',1,1,1,SYSUTCDATETIME()),
                  (@Meat,@TenantId,@MeatCode,N'EA',@TaxProfileId,N'Carne',N'COP',1,1,1,SYSUTCDATETIME()),
                  (@Burger,@TenantId,@BurgerCode,N'EA',@TaxProfileId,N'Hamburguesa preparada',N'COP',1,0,1,SYSUTCDATETIME());
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            SELECT @BusinessId,w.WarehouseId,p.ProductId,0,0,0,
                   COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),SYSDATETIMEOFFSET()
            FROM dbo.Warehouses w CROSS JOIN dbo.Products p
            WHERE w.BusinessId=@BusinessId AND p.ProductId IN(@Bread,@Meat,@Burger)
              AND NOT EXISTS(SELECT 1 FROM dbo.InventoryBalances b WHERE b.BusinessId=@BusinessId AND b.WarehouseId=w.WarehouseId AND b.ProductId=p.ProductId);
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TaxCode", $"IVA-{burger:N}"[..32]);
        command.Parameters.AddWithValue("@Bread", bread);
        command.Parameters.AddWithValue("@Meat", meat);
        command.Parameters.AddWithValue("@Burger", burger);
        command.Parameters.AddWithValue("@BreadCode", $"PAN-{bread:N}"[..32]);
        command.Parameters.AddWithValue("@MeatCode", $"CAR-{meat:N}"[..32]);
        command.Parameters.AddWithValue("@BurgerCode", $"HAM-{burger:N}"[..32]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InventoryOperationAcceptance> SendAsync<T>(HttpClient client, string path, T payload, string key)
    {
        using var response = await SendRawAsync(client, path, payload, key);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InventoryOperationAcceptance>())!;
    }

    private static Task<HttpResponseMessage> SendRawAsync<T>(HttpClient client, string path, T payload, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        message.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(message);
    }

    private async Task<decimal> BalanceAsync(Guid productId) => await ScalarAsync<decimal>(
        "SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@Id", productId);

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
