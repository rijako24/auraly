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
                new ProductPriceInput(priceChannelId, 12_500m),
                new ProductPriceInput(secondChannelId, 99_999m)
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
        Assert.Single(suppliers);
        var filterUri = $"/api/commerce/v1/products?barcode=7701234500012&supplierId={suppliers.Single().SupplierId:D}&priceChannelId={priceChannelId:D}&minimumPrice=12000&maximumPrice=13000&sortDescending=true";
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
            Assert.Equal(priceChannelId, await ScalarAsync<Guid>(
                "SELECT PriceChannelId FROM dbo.CashRegisters WHERE RegisterId=@Id;",
                new SqlParameter("@Id", fixture.RegisterId)));
            Assert.Equal(0, await LocalScalarAsync(
                sqlitePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%Supplier%';"));

            var updated = request with
            {
                Name = "Coffee updated",
                Barcodes = [new ProductBarcodeInput("7701234500098", true)],
                Prices =
                [
                    new ProductPriceInput(priceChannelId, 13_250m),
                    new ProductPriceInput(secondChannelId, 99_999m)
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
            [new ProductPriceInput(priceChannelId, 10_000m)],
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
            request with { TenantId = Guid.NewGuid(), ProductCode = $"OTHER-{Guid.NewGuid():N}" });
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
              INSERT dbo.TaxProfiles(TaxProfileId,TenantId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
              VALUES(@Tax,@Tenant,@Business,N'VAT19',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Channel)
              INSERT dbo.PriceChannels(PriceChannelId,TenantId,BusinessId,Code,Name,IsDefault,IsActive,CreatedAt)
              VALUES(@Channel,@Tenant,@Business,N'POS',N'POS',1,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Second)
              INSERT dbo.PriceChannels(PriceChannelId,TenantId,BusinessId,Code,Name,IsDefault,IsActive,CreatedAt)
              VALUES(@Second,@Tenant,@Business,N'WHOLESALE',N'Wholesale',0,1,SYSDATETIMEOFFSET());
            UPDATE dbo.CashRegisters SET PriceChannelId=@Channel WHERE RegisterId=@Register;
            IF NOT EXISTS (SELECT 1 FROM dbo.PosDevicePermissions WHERE DeviceId=@Device AND PermissionCode=@Permission)
              INSERT dbo.PosDevicePermissions(DeviceId,PermissionCode,IsGranted,GrantedAt)
              VALUES(@Device,@Permission,1,SYSDATETIMEOFFSET());
            """;
        await ExecuteAsync(
            sql,
            new SqlParameter("@Tax", tax),
            new SqlParameter("@Channel", channel),
            new SqlParameter("@Second", second),
            new SqlParameter("@Tenant", fixture.TenantId),
            new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@Register", fixture.RegisterId),
            new SqlParameter("@Device", fixture.DeviceId),
            new SqlParameter("@Permission", CatalogPermissionCodes.Sync));
        return (tax, channel, second);
    }

    private SaveProductRequest ProductRequest(
        Guid taxProfileId,
        IReadOnlyCollection<ProductPriceInput> prices,
        IReadOnlyCollection<ProductBarcodeInput> barcodes) =>
        new(
            fixture.TenantId,
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
