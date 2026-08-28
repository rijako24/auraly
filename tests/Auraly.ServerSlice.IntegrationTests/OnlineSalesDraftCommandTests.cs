using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesDraftCommandTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Online_capture_uses_customer_pricing_and_supports_line_commands()
    {
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var priceChannelId = Guid.NewGuid();
        var priceChannelItemId = Guid.NewGuid();
        var volumePriceChannelItemId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var subgroupId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var categoryExclusionId = Guid.NewGuid();
        var barcodeId = Guid.NewGuid();
        var barcode = $"770{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Online',
              1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,
              CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'NaturalPerson',N'Cliente canal online',
              N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.PriceChannels(
              PriceChannelId,BusinessId,Code,Name,Strategy,IsActive,CreatedAt)
            VALUES(
              @PriceChannelId,@BusinessId,@ChannelCode,N'Canal online',N'TieredProductPrice',1,SYSDATETIMEOFFSET());
            INSERT dbo.ResolvedPriceChannelItems(
              ResolvedPriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
              CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)
            VALUES(
              @PriceChannelItemId,@PriceChannelId,@ProductId,1,8000,N'COP',
              DATEADD(day,-2,SYSDATETIMEOFFSET()),DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            INSERT dbo.ResolvedPriceChannelItems(
              ResolvedPriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
              CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(
              @VolumePriceChannelItemId,@PriceChannelId,@ProductId,3,7000,N'COP',
              DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            INSERT dbo.CustomerPricingSettings(
              CustomerId,PriceChannelId,UpdatedBy,UpdatedAt)
            VALUES(
              @CustomerId,@PriceChannelId,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.ProductCategories(ProductCategoryId,BusinessId,Name,CreatedAt)
            VALUES(@AreaId,@BusinessId,N'Área excluible',SYSUTCDATETIME());
            INSERT dbo.ProductCategories(
              ProductCategoryId,BusinessId,ParentProductCategoryId,Name,CreatedAt)
            VALUES(@SubgroupId,@BusinessId,@AreaId,N'Subgrupo excluible',SYSUTCDATETIME());
            INSERT dbo.ProductBrands(ProductBrandId,BusinessId,Name,IsActive,CreatedAt)
            VALUES(@BrandId,@BusinessId,N'Marca excluible',1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products
            SET ProductCategoryId=@SubgroupId, ProductBrandId=@BrandId
            WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
            INSERT dbo.ProductBarcodes(
              ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES(
              @BarcodeId,@BusinessId,@ProductId,@Barcode,1,1,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"online-{userId:N}"),
            new("@NormalizedUsername", $"ONLINE-{userId:N}".ToUpperInvariant()),
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@BusinessId", fixture.BusinessId),
            new("@PriceChannelId", priceChannelId),
            new("@ChannelCode", $"C-{priceChannelId:N}"[..20]),
            new("@PriceChannelItemId", priceChannelItemId),
            new("@VolumePriceChannelItemId", volumePriceChannelItemId),
            new("@AreaId", areaId),
            new("@SubgroupId", subgroupId),
            new("@BrandId", brandId),
            new("@ProductId", fixture.ProductId),
            new("@BarcodeId", barcodeId),
            new("@Barcode", barcode));

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesDiscount,
            CommercePermissionCodes.SalesRemoveLine,
            "orders.create",
            WorkSessionPermissionCodes.Open);
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenAsync(client, workSession.WorkSessionId);
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId, fixture.WarehouseId, workSession.WorkSessionId);

        using (var searchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            searchResponse.EnsureSuccessStatusCode();
            var products = await searchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(8_000m, product.UnitPrice);
            Assert.Equal("PriceChannel", product.PriceSource);
        }

        using (var catalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            catalogResponse.EnsureSuccessStatusCode();
            var catalog = await catalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(8_000m, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("PriceChannel", product.GetProperty("priceSource").GetString());
        }

        await ExecuteAsync(
            "UPDATE dbo.PriceChannels SET Strategy=N'ProductMarginAdjustment',Value=10 WHERE PriceChannelId=@PriceChannelId;",
            new SqlParameter("@PriceChannelId", priceChannelId));
        decimal salesAdjustmentPrice;
        using (var adjustedSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            adjustedSearchResponse.EnsureSuccessStatusCode();
            var products = await adjustedSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            salesAdjustmentPrice = product.UnitPrice;
            Assert.True(salesAdjustmentPrice > 0);
            Assert.Equal("PriceChannel", product.PriceSource);
        }
        using (var adjustedCatalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            adjustedCatalogResponse.EnsureSuccessStatusCode();
            var catalog = await adjustedCatalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(salesAdjustmentPrice, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("PriceChannel", product.GetProperty("priceSource").GetString());
        }
        await ExecuteAsync(
            "UPDATE dbo.PriceChannels SET Strategy=N'TieredProductPrice',Value=NULL WHERE PriceChannelId=@PriceChannelId;",
            new SqlParameter("@PriceChannelId", priceChannelId));

        await ExecuteAsync(
            """
            INSERT dbo.PriceChannelExclusions(
              PriceChannelExclusionId,PriceChannelId,ScopeType,ProductCategoryId,CreatedAt)
            VALUES(@ExclusionId,@PriceChannelId,N'Category',@AreaId,SYSDATETIMEOFFSET());
            """,
            new("@ExclusionId", categoryExclusionId),
            new("@PriceChannelId", priceChannelId),
            new("@AreaId", areaId));

        using (var excludedSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            excludedSearchResponse.EnsureSuccessStatusCode();
            var products = await excludedSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(10_000m, product.UnitPrice);
            Assert.Equal("Base", product.PriceSource);
        }

        using (var excludedCatalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            excludedCatalogResponse.EnsureSuccessStatusCode();
            var catalog = await excludedCatalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(10_000m, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("Base", product.GetProperty("priceSource").GetString());
        }

        await ExecuteAsync(
            "DELETE dbo.PriceChannelExclusions WHERE PriceChannelExclusionId=@ExclusionId;",
            new SqlParameter("@ExclusionId", categoryExclusionId));

        var captured = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(barcode, 1m, draft.Version));
        var baseLine = Assert.Single(captured.Lines);
        Assert.Equal(10_000m, baseLine.UnitPrice);
        Assert.Equal("Base", baseLine.PriceSource);

        var selected = await MutateAsync<OnlineSalesCustomerSelection>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer",
            new SelectOnlineSalesDraftCustomerRequest(customerId, captured.Version));
        Assert.NotNull(selected.Customer);
        Assert.Equal(customerId, selected.Customer.CustomerId);
        var customerLine = Assert.Single(selected.Draft.Lines);
        Assert.Equal(8_000m, customerLine.UnitPrice);
        Assert.Equal("PriceChannel", customerLine.PriceSource);

        var discounted = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/discount",
            new SetOnlineSalesDraftDiscountRequest(1_000m, selected.Draft.Version));
        Assert.Equal(7_000m, discounted.PayableAmount);

        var changed = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/quantity",
            new ChangeOnlineSalesDraftQuantityRequest(2m, discounted.Version));
        Assert.Equal(15_000m, changed.PayableAmount);

        var volumePriced = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(barcode, 1m, changed.Version));
        Assert.Equal(2, volumePriced.Lines.Count);
        Assert.All(volumePriced.Lines, line => Assert.Equal(7_000m, line.UnitPrice));
        Assert.Equal(20_000m, volumePriced.PayableAmount);

        var removed = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/remove",
            new RemoveOnlineSalesDraftLineRequest(volumePriced.Version));
        var remaining = Assert.Single(removed.Lines);
        Assert.Equal(8_000m, remaining.UnitPrice);
        Assert.Equal(8_000m, removed.PayableAmount);

        var emptied = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{remaining.LineId:D}/remove",
            new RemoveOnlineSalesDraftLineRequest(removed.Version));
        Assert.Empty(emptied.Lines);
        Assert.Equal(0m, emptied.PayableAmount);
    }

    [Fact]
    public async Task Online_capture_respects_warehouse_negative_policy()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Inventario',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Warehouses
            SET AllowNegativeStockSales=1
            WHERE WarehouseId=@WarehouseId;
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"stock-{userId:N}"),
            new("@NormalizedUsername", $"STOCK-{userId:N}".ToUpperInvariant()),
            new("@WarehouseId", fixture.WarehouseId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId,
                CommercePermissionCodes.SalesCreate,
                WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            var captured = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1m, draft.Version));
            var line = Assert.Single(captured.Lines);
            await ExecuteAsync(
                "UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@WarehouseId;",
                new SqlParameter("@WarehouseId", fixture.WarehouseId));
            using var request = Mutation(
                HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(999_999m, captured.Version));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadAsStringAsync();
            Assert.Contains("Inventario insuficiente", problem, StringComparison.Ordinal);
            Assert.Contains("Disponible:", problem, StringComparison.Ordinal);
            var unchanged = await OpenAsync(client, workSession.WorkSessionId);
            Assert.Equal(1m, Assert.Single(unchanged.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                """
                UPDATE dbo.Warehouses
                SET AllowNegativeStockSales=1
                WHERE WarehouseId=@WarehouseId;
                """,
                new SqlParameter("@WarehouseId", fixture.WarehouseId));
        }
    }

    [Fact]
    public async Task Online_capture_does_not_validate_inventory_for_non_stock_products()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Sin inventario',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@WarehouseId;
            UPDATE dbo.Products SET ManageStock=0 WHERE ProductId=@ProductId;
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"no-stock-{userId:N}"),
            new("@NormalizedUsername", $"NO-STOCK-{userId:N}".ToUpperInvariant()),
            new("@WarehouseId", fixture.WarehouseId),
            new("@ProductId", fixture.ProductId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            var captured = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 999_999m, draft.Version));
            var line = Assert.Single(captured.Lines);
            Assert.Equal(999_999m, line.Quantity);

            var changed = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(1_000_000m, captured.Version));
            Assert.Equal(1_000_000m, Assert.Single(changed.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                """
                UPDATE dbo.Products SET ManageStock=1 WHERE ProductId=@ProductId;
                UPDATE dbo.Warehouses SET AllowNegativeStockSales=1 WHERE WarehouseId=@WarehouseId;
                """,
                new("@ProductId", fixture.ProductId),
                new("@WarehouseId", fixture.WarehouseId));
        }
    }

    [Fact]
    public async Task Online_capture_allows_decimals_only_for_fractional_products()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Fraccionada',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products SET AllowsFractionalSale=0 WHERE ProductId=@ProductId;
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"fraction-{userId:N}"),
            new("@NormalizedUsername", $"FRACTION-{userId:N}".ToUpperInvariant()),
            new("@ProductId", fixture.ProductId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            using var rejectedRequest = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1.5m, draft.Version));
            using var rejected = await client.SendAsync(rejectedRequest);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            await ExecuteAsync(
                "UPDATE dbo.Products SET AllowsFractionalSale=1 WHERE ProductId=@ProductId;",
                new SqlParameter("@ProductId", fixture.ProductId));
            var accepted = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1.5m, draft.Version));
            Assert.Equal(1.5m, Assert.Single(accepted.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                "UPDATE dbo.Products SET AllowsFractionalSale=0 WHERE ProductId=@ProductId;",
                new SqlParameter("@ProductId", fixture.ProductId));
        }
    }

    private async Task<OnlineSalesDraft> OpenAsync(HttpClient client, Guid workSessionId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                workSessionId)));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private static async Task<T> MutateAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        using var request = Mutation(method, path, body);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("Empty mutation response.");
    }

    private static HttpRequestMessage Mutation(
        HttpMethod method,
        string path,
        object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
