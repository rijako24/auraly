using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class CatalogVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Product_is_administered_once_and_synchronized_to_physical_pos_sqlite()
    {
        var (taxProfileId, priceChannelId, secondChannelId) = await ConfigureCatalogAsync();
        var request = ProductRequest(
            taxProfileId,
            [
                new ProductPriceInput(12_500m)
            ],
            [new ProductBarcodeInput("7701234500012", true)]);

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Deactivate,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ReadCosts,
            CatalogPermissionCodes.ManageCosts);
        using var create = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.NotNull(created);
        var suppliers = created.Suppliers!;
        var priceListId = Guid.NewGuid();
        var listItemId = Guid.NewGuid();
        var channelItemId = Guid.NewGuid();
        var listCustomerId = Guid.NewGuid();
        var channelCustomerId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.PriceLists(PriceListId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@List,@Business,N'VIP',N'VIP',1,SYSDATETIMEOFFSET());
            INSERT dbo.PriceListItems
              (PriceListItemId,PriceListId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
              VALUES(@ListItem,@List,@Product,1,11000,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            INSERT dbo.ResolvedPriceChannelItems
              (ResolvedPriceChannelItemId,PriceChannelId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
              VALUES(@ChannelItem,@Channel,@Product,11500,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceCustomers
              (CustomerId,BusinessId,IdentificationType,Identification,Name,IsActive,CreatedAt)
              VALUES
              (@ListCustomer,@Business,N'CC',N'1001',N'List customer',1,SYSDATETIMEOFFSET()),
              (@ChannelCustomer,@Business,N'CC',N'1002',N'Channel customer',1,SYSDATETIMEOFFSET());
            INSERT dbo.CustomerBusinessPricing(CustomerId,PriceListId,PriceChannelId,UpdatedAt)
              VALUES
              (@ListCustomer,@List,NULL,SYSDATETIMEOFFSET()),
              (@ChannelCustomer,NULL,@Channel,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@List", priceListId),
            new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@ListItem", listItemId),
            new SqlParameter("@ChannelItem", channelItemId),
            new SqlParameter("@Channel", priceChannelId),
            new SqlParameter("@Product", created.ProductId),
            new SqlParameter("@ListCustomer", listCustomerId),
            new SqlParameter("@ChannelCustomer", channelCustomerId));
        Assert.Single(suppliers);
        var filterUri = $"/api/commerce/v1/products?barcode=7701234500012&supplierId={suppliers.Single().SupplierId:D}&minimumPrice=12000&maximumPrice=13000&sortDescending=true";
        var page = await admin.GetFromJsonAsync<ProductPage>(filterUri);
        Assert.NotNull(page);
        Assert.Contains(page.Items, product => product.ProductId == created.ProductId);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE ProductId=@Id;",
            new SqlParameter("@Id", created.ProductId)));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name=N'CatalogProducts';"));

        var sqlitePath = Path.Combine(Path.GetTempPath(), $"auraly-pos-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            var sync = new PosCatalogSynchronizer(
                fixture.CreateClient(),
                local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret));
            await sync.SynchronizeAsync();

            var captured = await local.CaptureAsync("7701234500012");
            Assert.NotNull(captured);
            Assert.Equal(12_500m, captured.Product.UnitPrice);
            Assert.Equal(fixture.BusinessId, await ScalarAsync<Guid>(
                "SELECT BusinessId FROM dbo.ProductPrices WHERE ProductId=@Id AND IsActive=1;",
                new SqlParameter("@Id", created.ProductId)));
            Assert.Equal(0, await LocalScalarAsync(
                sqlitePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%Supplier%';"));

            var listPrice = await local.ResolvePriceAsync(created.ProductId, listCustomerId, 1m);
            Assert.Equal("PriceList", listPrice.Source);
            Assert.Equal(11_000m, listPrice.Amount);
            var channelPrice = await local.ResolvePriceAsync(created.ProductId, channelCustomerId, 1m);
            Assert.Equal("PriceChannel", channelPrice.Source);
            Assert.Equal(11_500m, channelPrice.Amount);
            Assert.Equal(12_500m, (await local.ResolvePriceAsync(created.ProductId, null, 1m)).Amount);

            var updated = request with
            {
                Name = "Coffee updated",
                Barcodes = [new ProductBarcodeInput("7701234500098", true)],
                Prices =
                [
                    new ProductPriceInput(13_250m)
                ]
            };
            using var update = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/products/{created.ProductId:D}",
                updated);
            update.EnsureSuccessStatusCode();

            await sync.SynchronizeAsync();
            Assert.Null(await local.CaptureAsync("7701234500012"));
            var changed = await local.CaptureAsync("7701234500098");
            Assert.NotNull(changed);
            Assert.Equal(13_250m, changed.Product.UnitPrice);

            using var deactivate = await admin.PostAsync(
                $"/api/commerce/v1/products/{created.ProductId:D}/deactivate",
                content: null);
            Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
            await sync.SynchronizeAsync();
            Assert.Null(await local.CaptureAsync("7701234500098"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }

    [Fact]
    public async Task Security_uniqueness_and_warehouse_negative_policy_are_enforced()
    {
        var (taxProfileId, priceChannelId, _) = await ConfigureCatalogAsync();
        var barcode = $"77{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var request = ProductRequest(
            taxProfileId,
            [new ProductPriceInput(10_000m)],
            [new ProductBarcodeInput(barcode, true)]) with
        {
            ProductCode = $"SEC-{Guid.NewGuid():N}"
        };

        using var denied = fixture.CreateAdminClient(CatalogPermissionCodes.Read);
        using var deniedResponse = await denied.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts);
        using var createdResponse = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<ProductDetail>())!;

        using var wrongScope = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            request with { BusinessId = Guid.NewGuid(), ProductCode = $"OTHER-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);

        using var duplicate = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            request with { ProductCode = $"DUP-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var sync = new PosCatalogSynchronizer(
            fixture.CreateClient(),
            new PosCatalogStore($"Data Source={Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.db")}"),
            new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret));
        var allowed = await sync.CheckAvailabilityAsync(
            new InventoryAvailabilityRequest(created.ProductId, fixture.WarehouseId, 1m, Guid.NewGuid()));
        Assert.False(allowed.ValidationRequired);
        Assert.True(allowed.IsAvailable);

        await ExecuteAsync(
            "UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@Id;",
            new SqlParameter("@Id", fixture.WarehouseId));
        var blocked = await sync.CheckAvailabilityAsync(
            new InventoryAvailabilityRequest(created.ProductId, fixture.WarehouseId, 1m, Guid.NewGuid()));
        Assert.True(blocked.ValidationRequired);
        Assert.False(blocked.IsAvailable);
    }

    private async Task<(Guid Tax, Guid Channel, Guid Second)> ConfigureCatalogAsync()
    {
        var tax = fixture.TaxProfileId;
        var channel = fixture.PriceChannelId;
        var second = Guid.Parse("019ad230-6e45-7a28-a71e-25584f52bd65");
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@Tax)
              INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
              VALUES(@Tax,@Business,N'VAT19',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Channel)
              INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@Channel,@Business,N'POS',N'POS',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Second)
              INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@Second,@Business,N'WHOLESALE',N'Wholesale',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PosDevicePermissions WHERE DeviceId=@Device AND PermissionCode=@Permission)
              INSERT dbo.PosDevicePermissions(DeviceId,PermissionCode,IsGranted,GrantedAt)
              VALUES(@Device,@Permission,1,SYSDATETIMEOFFSET());
            """;
        await ExecuteAsync(
            sql,
            new SqlParameter("@Tax", tax),
            new SqlParameter("@Channel", channel),
            new SqlParameter("@Second", second),
            new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@Device", fixture.DeviceId),
            new SqlParameter("@Permission", CatalogPermissionCodes.Sync));
        return (tax, channel, second);
    }

    private SaveProductRequest ProductRequest(
        Guid taxProfileId,
        IReadOnlyCollection<ProductPriceInput> prices,
        IReadOnlyCollection<ProductBarcodeInput> barcodes) =>
        new(
            fixture.BusinessId,
            $"P-{Guid.NewGuid():N}",
            "REF-COFFEE",
            "Coffee 500 g",
            "Ground coffee",
            "EA",
            taxProfileId,
            true,
            false,
            barcodes,
            [new ProductIdentifierInput("Alternate", $"ALT-{Guid.NewGuid():N}")],
            prices,
            [new SupplierCostInput(Guid.Empty, $"SUP-{Guid.NewGuid():N}", "Supplier", null, 8_000m)],
            null);

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = (await command.ExecuteScalarAsync())!;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }

    private static async Task<long> LocalScalarAsync(string path, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
