using System.Net;
using System.Net.Http.Json;
using System.Data;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Pricing;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class ProductionVerticalSliceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Composition_creation_does_not_require_component_prices_in_an_unoffered_business()
    {
        var ingredient=Guid.NewGuid();await SeedAsync(ingredient,Guid.NewGuid(),Guid.NewGuid());
        var otherBusiness=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var create=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES(@Business,@Tenant,N'Sede sin ese producto',N'',N'',N'',N'',N'',1,SYSDATETIMEOFFSET());
            SELECT TaxProfileId FROM dbo.Products WHERE ProductId=@Ingredient;
            """,connection);
        create.Parameters.AddWithValue("@Business",otherBusiness);create.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        create.Parameters.AddWithValue("@Ingredient",ingredient);
        var tax=(Guid)(await create.ExecuteScalarAsync())!;
        using var client=fixture.CreateAdminClient(CatalogPermissionCodes.Create,CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update,CatalogPermissionCodes.ManagePrices,CatalogPermissionCodes.ReadCosts,CatalogPermissionCodes.ManageCosts);
        var request=new SaveProductRequest(fixture.BusinessId,"",null,$"Preparado {Guid.NewGuid():N}",null,"EA",tax,true,false,[],[],
            [new ProductPriceInput(12500m,"COP",10000m,20m)],[new SupplierCostInput(fixture.SupplierId,"900999001","Proveedor",null,10000m)],null,tax,
            RecipeIngredients:[new(ingredient,2m)],RecipeExpectedVersion:0);
        using var response=await client.PostAsJsonAsync("/api/commerce/v1/products",request);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var product=(await response.Content.ReadFromJsonAsync<ProductDetail>())!;
        Assert.True(product.HasIngredients);Assert.Equal(2380m,Assert.Single(product.Prices).CostBasisAmount);
        create.CommandText="SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE BusinessId=@Business AND ProductId=@Product;";
        create.Parameters.AddWithValue("@Product",product.ProductId);
        Assert.Equal(0,Convert.ToInt32(await create.ExecuteScalarAsync()));
        using var cleared=await client.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}",request with
        {
            ProductCode=product.ProductCode,RecipeIngredients=[],RecipeExpectedVersion=1,
            Prices=[new ProductPriceInput(0m,"COP",3000m,20m,PreparedAmount:3750m)]
        });
        Assert.True(cleared.IsSuccessStatusCode,await cleared.Content.ReadAsStringAsync());
        Assert.False((await cleared.Content.ReadFromJsonAsync<ProductDetail>())!.HasIngredients);
        create.CommandText="SELECT CostBasisType FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND BusinessId=@Source AND Status=N'Pending';";
        create.Parameters.AddWithValue("@Source",fixture.BusinessId);
        Assert.Equal("Manual",await create.ExecuteScalarAsync());
    }

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
            CatalogPermissionCodes.ManagePrices, CatalogPermissionCodes.ReadCosts, CatalogPermissionCodes.ManageCosts, "sales.create");
        var name = $"Producto receta {Guid.NewGuid():N}";
        var request = new SaveProductRequest(fixture.BusinessId, "", null, name, null,
            "EA", fixture.TaxProfileId, true, false, [], [],
            [new ProductPriceInput(12_500m, "COP", 10_000m, 20m)],
            [new SupplierCostInput(fixture.SupplierId, "900999001", "Proveedor E2E", null, 10_000m)],
            null, fixture.TaxProfileId,
            RecipeIngredients: [new(bread, 2m)], RecipeExpectedVersion: 0);
        using var saved = await client.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.True(saved.StatusCode == HttpStatusCode.Created,
            $"Expected Created, received {saved.StatusCode}: {await saved.Content.ReadAsStringAsync()}");
        var product = (await saved.Content.ReadFromJsonAsync<ProductDetail>())!;
        Assert.Equal(0m, Assert.Single(product.Prices).Amount);
        Assert.Equal(2380m, Assert.Single(product.Prices).CostBasisAmount);
        Assert.Equal(3540m, Assert.Single(product.Prices).PreparedAmount);
        var salesContext = new Auraly.Contracts.Sales.OnlineSalesDraftContext(fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId);
        using var search = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/products/search",
            new Auraly.Contracts.Sales.SearchOnlineSalesRequest(salesContext, name));
        Assert.True(search.IsSuccessStatusCode, await search.Content.ReadAsStringAsync());
        var sellable = (await search.Content.ReadFromJsonAsync<Auraly.Contracts.Sales.OnlineSalesProductPage>())!;
        Assert.DoesNotContain(sellable.Items, item => item.ProductId == product.ProductId);
        Assert.Equal(0m, await BalanceAsync(bread));
        var recipe = await client.GetFromJsonAsync<ProductRecipeDetail>($"/api/commerce/v1/products/{product.ProductId:D}/recipe");
        Assert.Equal(bread, Assert.Single(recipe!.Ingredients).IngredientProductId);

        using (var updated = await client.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId:D}",
            request with { Name = name + " editado", Prices = product.Prices, RecipeExpectedVersion = 1,
                RecipeIngredients = [new(meat, 1m)] }))
            Assert.True(updated.StatusCode == HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
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

    [Fact]
    public async Task Combo_is_exclusive_cost_is_derived_with_vat_and_public_price_is_preserved()
    {
        var first=Guid.NewGuid(); var second=Guid.NewGuid(); var combo=Guid.NewGuid();
        var independentBusiness = Guid.NewGuid();
        await SeedAsync(first,second,combo);
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var setup=connection.CreateCommand();
        setup.CommandText="""
          UPDATE dbo.Products SET IsIngredient=0 WHERE ProductId IN(@First,@Second);
          DECLARE @Zero uniqueidentifier=NEWID();
          INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@Zero,@Tenant,@Code,N'Combo cero',0,1,SYSDATETIMEOFFSET());
          UPDATE dbo.Products SET TaxProfileId=@Zero WHERE ProductId=@Combo;
          INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            SELECT @Independent,@Tenant,N'Precios independientes',Description,Address,Phone,Email,Website,1,SYSDATETIMEOFFSET()
            FROM dbo.Businesses WHERE BusinessId=@Business;
          INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
            CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
          SELECT NEWID(),@Independent,ProductId,10000,10000,N'COP',2000,25,25,1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM dbo.Products WHERE ProductId IN(@First,@Second,@Combo);
          """;
        setup.Parameters.AddWithValue("@First",first); setup.Parameters.AddWithValue("@Second",second);
        setup.Parameters.AddWithValue("@Combo",combo); setup.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        setup.Parameters.AddWithValue("@Independent", independentBusiness);
        setup.Parameters.AddWithValue("@Business", fixture.BusinessId);
        setup.Parameters.AddWithValue("@Code",$"ZERO-{combo:N}"[..32]); await setup.ExecuteNonQueryAsync();
        using var client=fixture.CreateAdminClient(CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,
            CatalogPermissionCodes.ReadCosts);
        using var saved=await client.PutAsJsonAsync($"/api/commerce/v1/products/{combo:D}/recipe",
            new PublishProductRecipeRequest(0,[new(first,2m),new(second,1m)],"Combo"));
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var value=(await saved.Content.ReadFromJsonAsync<ProductRecipeDetail>())!;
        Assert.Equal("Combo",value.CompositionKind);
        await using var verify=connection.CreateCommand();
        verify.CommandText="""
          SELECT p.HasIngredients,p.IsCombo,price.Amount,prepared.CostBasisAmount,prepared.PreparedAmount
          FROM dbo.Products p JOIN dbo.ProductPrices price ON price.ProductId=p.ProductId AND price.BusinessId=@Business AND price.IsActive=1
          JOIN dbo.ProductPricePreparations prepared ON prepared.ProductId=p.ProductId AND prepared.BusinessId=@Business AND prepared.Status=N'Pending'
          WHERE p.ProductId=@Combo;
          """;
        verify.Parameters.AddWithValue("@Combo",combo);verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
        await using(var reader=await verify.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync()); Assert.False(reader.GetBoolean(0));Assert.True(reader.GetBoolean(1));
            Assert.Equal(10000m,reader.GetDecimal(2));Assert.Equal(3570m,reader.GetDecimal(3));Assert.Equal(4760m,reader.GetDecimal(4));
        }
        using var pricing = fixture.CreateAdminClient(PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis, PricingPermissionCodes.PreparePrices,
            PricingPermissionCodes.PublishPrices, PricingPermissionCodes.ReviewProposals);
        using var componentChange = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{first:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 25m, null, 1m,
                PricingRoundingModes.Nearest, 2000m));
        Assert.True(componentChange.IsSuccessStatusCode, await componentChange.Content.ReadAsStringAsync());
        await using (var reader = await verify.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(10000m, reader.GetDecimal(2));
            Assert.Equal(5950m, reader.GetDecimal(3));
            Assert.Equal(7933m, reader.GetDecimal(4));
        }
        using var manualCost = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{combo:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 25m, null, 1m,
                PricingRoundingModes.Nearest, 42m));
        Assert.Equal(HttpStatusCode.BadRequest, manualCost.StatusCode);
        var componentProposals = await pricing.GetFromJsonAsync<PriceRevisionPage>("/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Pending");
        var componentCandidate = Assert.Single(componentProposals!.Items, item => item.ProductId == first);
        using var componentDiscard = await pricing.PostAsJsonAsync($"/api/commerce/v1/pricing/proposals/{componentCandidate.ProposalId:D}/reject",
            new RejectPriceProposalRequest(componentCandidate.ConcurrencyToken, "Restablecer costo del componente"));
        Assert.True(componentDiscard.IsSuccessStatusCode, await componentDiscard.Content.ReadAsStringAsync());
        var reverted = await pricing.GetFromJsonAsync<ProductPricingContext>($"/api/commerce/v1/pricing/products/{combo:D}/context");
        Assert.Equal(3570m, reverted!.CostBasisAmount);
        Assert.Equal(10000m, reverted.PublicSalePrice);
        using var restored = await pricing.PutAsJsonAsync($"/api/commerce/v1/pricing/products/{first:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 25m, null, 1m, PricingRoundingModes.Nearest, 2000m));
        Assert.True(restored.IsSuccessStatusCode, await restored.Content.ReadAsStringAsync());
        await using var isolated = new SqlCommand("""
            SELECT CostBasisAmount FROM dbo.ProductPricePreparations
            WHERE BusinessId=@Business AND ProductId=@Product AND Status=N'Pending';
            """, connection);
        isolated.Parameters.AddWithValue("@Business", independentBusiness);
        isolated.Parameters.AddWithValue("@Product", combo);
        Assert.Equal(7140m, (decimal)(await isolated.ExecuteScalarAsync())!);
        var proposals = await pricing.GetFromJsonAsync<PriceRevisionPage>("/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Pending");
        var candidate = Assert.Single(proposals!.Items, item => item.ProductId == combo);
        Assert.Equal("Composition", candidate.Origin);
        using var discard = await pricing.PostAsJsonAsync($"/api/commerce/v1/pricing/proposals/{candidate.ProposalId:D}/reject",
            new RejectPriceProposalRequest(candidate.ConcurrencyToken, "Intento de costo manual"));
        Assert.Equal(HttpStatusCode.Conflict, discard.StatusCode);
        using var publication = await pricing.PostAsJsonAsync("/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([combo]));
        Assert.True(publication.IsSuccessStatusCode, await publication.Content.ReadAsStringAsync());
        var publicContext = await pricing.GetFromJsonAsync<ProductPricingContext>(
            $"/api/commerce/v1/pricing/products/{combo:D}/context");
        Assert.Equal(7933m, publicContext!.PublicSalePrice);
        Assert.Equal(5950m, publicContext.CostBasisAmount);
        using var cleared=await client.PutAsJsonAsync($"/api/commerce/v1/products/{combo:D}/recipe",
            new PublishProductRecipeRequest(1,[],"Combo"));
        Assert.True(cleared.IsSuccessStatusCode,await cleared.Content.ReadAsStringAsync());
        verify.CommandText="SELECT HasIngredients,IsCombo FROM dbo.Products WHERE ProductId=@Combo";
        await using var flags=await verify.ExecuteReaderAsync();Assert.True(await flags.ReadAsync());
        Assert.False(flags.GetBoolean(0));Assert.False(flags.GetBoolean(1));
    }

    [Fact]
    public async Task Combo_rejects_ingredient_members_and_nonzero_parent_tax_atomically()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var parent=Guid.NewGuid();
        await SeedAsync(first,second,parent);
        using var client=fixture.CreateAdminClient(CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/products/{parent:D}/recipe",
            new PublishProductRecipeRequest(0,[new(first,1m)],"Combo"));
        Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var finished = new SqlCommand("UPDATE dbo.Products SET IsIngredient=0 WHERE ProductId=@Product", connection);
        finished.Parameters.AddWithValue("@Product", first);
        await finished.ExecuteNonQueryAsync();
        using var invalidTax = await client.PutAsJsonAsync($"/api/commerce/v1/products/{parent:D}/recipe",
            new PublishProductRecipeRequest(0,[new(first,1m)],"Combo"));
        Assert.Equal(HttpStatusCode.Conflict, invalidTax.StatusCode);
        using var absent=await client.GetAsync($"/api/commerce/v1/products/{parent:D}/recipe");
        Assert.Equal(HttpStatusCode.NotFound,absent.StatusCode);
    }

    [Fact]
    public async Task Composition_cost_propagates_through_linked_presentations_without_adding_vat_twice()
    {
        var raw = Guid.NewGuid(); var finished = Guid.NewGuid(); var combo = Guid.NewGuid(); var presentation = Guid.NewGuid();
        await SeedAsync(raw, finished, combo);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            UPDATE dbo.Products SET IsIngredient=0 WHERE ProductId=@Finished;
            DECLARE @Zero UNIQUEIDENTIFIER=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
              VALUES(@Zero,@Tenant,@Code,N'Combo cero',0,1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products SET TaxProfileId=@Zero WHERE ProductId=@Combo;
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,TaxProfileId,PurchaseTaxProfileId,Name,Currency,ManageStock,IsActive,CreatedAt)
            SELECT @Presentation,@Tenant,CONCAT(N'P-',@Presentation),N'EA',TaxProfileId,PurchaseTaxProfileId,
              N'Presentación terminada',N'COP',1,1,SYSDATETIMEOFFSET() FROM dbo.Products WHERE ProductId=@Finished;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,CostBasisAmount,TargetMarginPercent,
              EffectiveMarginPercent,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),BusinessId,@Presentation,10000,N'COP',1190,25,25,1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
              FROM dbo.Businesses WHERE TenantId=@Tenant AND IsActive=1;
            INSERT dbo.ProductLinks(ProductLinkId,TenantId,ChildProductId,ParentProductId,SharesInventory,SharesPrice,PriceFactor,IsActive,CreatedAt)
              VALUES(NEWID(),@Tenant,@Presentation,@Finished,0,1,1,1,SYSDATETIMEOFFSET());
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId); seed.Parameters.AddWithValue("@Finished", finished);
        seed.Parameters.AddWithValue("@Combo", combo); seed.Parameters.AddWithValue("@Presentation", presentation);
        seed.Parameters.AddWithValue("@Code", $"ZERO-{combo:N}"[..32]); await seed.ExecuteNonQueryAsync();
        using var catalog = fixture.CreateAdminClient(CatalogPermissionCodes.Read, CatalogPermissionCodes.Update);
        using var recipe = await catalog.PutAsJsonAsync($"/api/commerce/v1/products/{finished:D}/recipe",
            new PublishProductRecipeRequest(0, [new(raw, 1m)]));
        Assert.True(recipe.IsSuccessStatusCode, await recipe.Content.ReadAsStringAsync());
        using var comboSaved = await catalog.PutAsJsonAsync($"/api/commerce/v1/products/{combo:D}/recipe",
            new PublishProductRecipeRequest(0, [new(presentation, 1m)], "Combo"));
        Assert.True(comboSaved.IsSuccessStatusCode, await comboSaved.Content.ReadAsStringAsync());
        using var pricing = fixture.CreateAdminClient(PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis, PricingPermissionCodes.PreparePrices);
        var before = await pricing.GetFromJsonAsync<ProductPricingContext>($"/api/commerce/v1/pricing/products/{combo:D}/context");
        Assert.Equal(1190m, before!.CostBasisAmount);
        using var changed = await pricing.PutAsJsonAsync($"/api/commerce/v1/pricing/products/{raw:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 25m, null, 1m, PricingRoundingModes.Nearest, 2000m));
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        var after = await pricing.GetFromJsonAsync<ProductPricingContext>($"/api/commerce/v1/pricing/products/{combo:D}/context");
        Assert.Equal(2380m, after!.CostBasisAmount);
        Assert.Equal(10000m, after.PublicSalePrice);
        using var cleared = await catalog.PutAsJsonAsync($"/api/commerce/v1/products/{finished:D}/recipe",
            new PublishProductRecipeRequest(1, []));
        Assert.True(cleared.IsSuccessStatusCode, await cleared.Content.ReadAsStringAsync());
        using var marginOnly = await pricing.PutAsJsonAsync($"/api/commerce/v1/pricing/products/{finished:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 30m, null, 1m, PricingRoundingModes.Nearest, 2380m));
        Assert.True(marginOnly.IsSuccessStatusCode, await marginOnly.Content.ReadAsStringAsync());
        var retained = await pricing.GetFromJsonAsync<ProductPricingContext>($"/api/commerce/v1/pricing/products/{combo:D}/context");
        Assert.Equal(2380m, retained!.CostBasisAmount);
    }

    [Fact]
    public async Task Composition_rows_show_canonical_tax_inclusive_cost_without_leaking_cost_permissions()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var parent = Guid.NewGuid();
        await SeedAsync(first, second, parent);
        var search = $"VISIBLE-{parent:N}";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            UPDATE dbo.Products SET ProductCode=CONCAT(@Search,N'-A') WHERE ProductId=@First;
            UPDATE dbo.Products SET ProductCode=CONCAT(@Search,N'-B'),PurchaseTaxTreatment=N'CapitalizedCost'
              WHERE ProductId=@Second;
            """, connection);
        seed.Parameters.AddWithValue("@Search", search);
        seed.Parameters.AddWithValue("@First", first);
        seed.Parameters.AddWithValue("@Second", second);
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Read, CatalogPermissionCodes.ReadCosts,
            CatalogPermissionCodes.Update);
        using var published = await client.PutAsJsonAsync($"/api/commerce/v1/products/{parent:D}/recipe",
            new PublishProductRecipeRequest(0, [new(first, 2m), new(second, 3m)]));
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync());
        var saved = (await published.Content.ReadFromJsonAsync<ProductRecipeDetail>())!;
        Assert.Equal(1190m, saved.Ingredients.Single(row => row.IngredientProductId == first).UnitCostWithTax);
        Assert.Equal(1000m, saved.Ingredients.Single(row => row.IngredientProductId == second).UnitCostWithTax);
        using var preview = await client.PostAsJsonAsync("/api/commerce/v1/products/composition-cost",
            new[] { new ProductRecipeIngredientInput(first, 2m), new(second, 3m) });
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var total = await preview.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(saved.Ingredients.Sum(row => row.QuantityPerUnit * row.UnitCostWithTax!.Value),
            total.GetProperty("costBasisAmount").GetDecimal());
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.Parameters.Contains("@CanReadCosts")))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var page = (await client.GetFromJsonAsync<ProductRecipeIngredientPage>(
                $"/api/commerce/v1/products/recipe-ingredients?search={search}&page=1&pageSize=1"))!;
            watch.Stop();
            Assert.Equal(2, page.TotalCount); Assert.Equal(2, page.TotalPages);
            Assert.NotNull(Assert.Single(page.Items).UnitCostWithTax);
            Assert.Equal(1, counter.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Cost option page exceeded 1 second: {watch.Elapsed}.");
            output.WriteLine($"Composition cost option page: {watch.Elapsed.TotalMilliseconds:F1} ms, {counter.Count} SQL command.");
        }
        using var readOnly = fixture.CreateAdminClient(CatalogPermissionCodes.Read);
        var hidden = (await readOnly.GetFromJsonAsync<ProductRecipeDetail>($"/api/commerce/v1/products/{parent:D}/recipe"))!;
        Assert.All(hidden.Ingredients, row => Assert.Null(row.UnitCostWithTax));
        var hiddenOptions = (await readOnly.GetFromJsonAsync<ProductRecipeIngredientPage>(
            $"/api/commerce/v1/products/recipe-ingredients?search={search}&page=1&pageSize=10"))!;
        Assert.Equal(2, hiddenOptions.TotalCount);
        Assert.All(hiddenOptions.Items, row => Assert.Null(row.UnitCostWithTax));
        seed.CommandText = "UPDATE dbo.ProductPrices SET IsActive=0 WHERE BusinessId=@Business AND ProductId=@First;";
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        await seed.ExecuteNonQueryAsync();
        var missing = (await client.GetFromJsonAsync<ProductRecipeDetail>($"/api/commerce/v1/products/{parent:D}/recipe"))!;
        Assert.Null(missing.Ingredients.Single(row => row.IngredientProductId == first).UnitCostWithTax);
        using var invalid = await client.PostAsJsonAsync("/api/commerce/v1/products/composition-cost",
            new[] { new ProductRecipeIngredientInput(first, 1m) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Composition_cost_preview_handles_one_hundred_components_in_one_request()
    {
        var components = Enumerable.Range(0, 100).Select(_ => new ProductRecipeIngredientInput(Guid.NewGuid(), 1m)).ToArray();
        await SeedAsync(components[0].IngredientProductId, components[1].IngredientProductId, Guid.NewGuid());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,TaxProfileId,PurchaseTaxProfileId,
              Name,Currency,ManageStock,IsIngredient,IsActive,CreatedAt)
            SELECT component.ProductId,@Tenant,CONCAT(N'PERF-',component.ProductId),N'EA',source.TaxProfileId,source.PurchaseTaxProfileId,
              N'Componente',N'COP',1,1,1,SYSDATETIMEOFFSET()
            FROM OPENJSON(@Components) WITH(ProductId UNIQUEIDENTIFIER '$.IngredientProductId') component
            CROSS JOIN dbo.Products source WHERE source.ProductId=@Source;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,CostBasisAmount,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),@Business,component.ProductId,10000,N'COP',1000,SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM OPENJSON(@Components) WITH(ProductId UNIQUEIDENTIFIER '$.IngredientProductId') component;
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        seed.Parameters.AddWithValue("@Source", components[0].IngredientProductId);
        seed.Parameters.AddWithValue("@Components", System.Text.Json.JsonSerializer.Serialize(components.Skip(2)));
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.ReadCosts);
        using var warm = await client.PostAsJsonAsync("/api/commerce/v1/products/composition-cost", components);
        Assert.True(warm.IsSuccessStatusCode, await warm.Content.ReadAsStringAsync());
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/products/composition-cost", components);
        elapsed.Stop();
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var value = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(119000m, value.GetProperty("costBasisAmount").GetDecimal());
        output.WriteLine($"Composition preview: 100 components, one HTTP request, {elapsed.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"Preview exceeded 5 seconds: {elapsed.Elapsed}.");
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
            UPDATE dbo.Products SET PurchaseTaxProfileId=@TaxProfileId WHERE ProductId IN(@Bread,@Meat,@Burger);
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
              CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),businessValue.BusinessId,product.ProductId,10000,10000,N'COP',1000,25,25,1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM dbo.Products product CROSS JOIN dbo.Businesses businessValue
            WHERE product.ProductId IN(@Bread,@Meat,@Burger)
              AND businessValue.TenantId=@TenantId AND businessValue.IsActive=1;
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
