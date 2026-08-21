using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Pricing;
using Auraly.Contracts.Purchasing;
using Auraly.Pos.Edge.Infrastructure;
using Auraly.Infrastructure.Persistence;
using Auraly.Infrastructure.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ProductInventoryOfflineJourneyTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task New_product_crosses_receipt_count_adjustment_kardex_and_offline_pos_catalog()
    {
        fixture.DrainSynchronizationMessages();
        await EnsureConfigurationAsync();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        using var catalog = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Deactivate,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ReadCosts,
            CatalogPermissionCodes.ManageCosts);
        var createRequest = new SaveProductRequest(
            fixture.BusinessId,
            $"FLOW-{Guid.NewGuid():N}",
            "FLOW-REFERENCE",
            "Producto flujo inventario offline",
            "Producto creado desde cero para validar la rebanada completa",
            "EA",
            fixture.TaxProfileId,
            true,
            false,
            [new ProductBarcodeInput(barcode, true)],
            [],
            [new ProductPriceInput(12_500m, "COP", 10_000m, 20m)],
            [new SupplierCostInput(Guid.Empty, $"SUP-{Guid.NewGuid():N}", "Proveedor flujo", null, 10_000m)],
            null);
        using var createdResponse = await catalog.PostAsJsonAsync(
            "/api/commerce/v1/products", createRequest);
        var createBody = await createdResponse.Content.ReadAsStringAsync();
        Assert.True(createdResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created, received {createdResponse.StatusCode}: {createBody}");
        var product = await createdResponse.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.NotNull(product);
        var supplier = Assert.Single(product.Suppliers!);
        var createdSignal = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal("Catalog", createdSignal.Stream);

        var occurredAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(-5));
        var receiptId = Guid.NewGuid();
        using var inventory = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.ReadCosts);
        var receipt = new ConfirmGoodsReceiptRequest(
            receiptId,
            fixture.BusinessId,
            fixture.WarehouseId,
            supplier.SupplierId,
            $"INV-{receiptId:N}",
            occurredAt.AddDays(-1),
            occurredAt,
            false,
            null,
            "COP",
            "Entrada de diez unidades",
            [new GoodsReceiptLineRequest(
                1,
                product.ProductId,
                product.Name,
                10m,
                10m,
                0m,
                "01",
                19m,
                PurchasingTaxTreatments.DeductibleInputVat)],
            null);
        await SendAcceptedAsync(inventory, "/api/commerce/v1/goods-receipts/confirm",
            receipt, $"receipt-{receiptId:N}");
        Assert.Equal(10m, await QuantityAsync(product.ProductId));

        var countId = Guid.NewGuid();
        using (var start = await inventory.PostAsJsonAsync(
                   "/api/commerce/v1/stock-counts/start",
                   new StartStockCountRequest(
                       countId,
                       fixture.BusinessId,
                       fixture.WarehouseId,
                       occurredAt.AddMinutes(1),
                       "PHYSICAL_COUNT",
                       "Conteo físico de ocho unidades",
                       [new StartStockCountLineRequest(product.ProductId, 9m)])))
        {
            start.EnsureSuccessStatusCode();
            var draft = await start.Content.ReadFromJsonAsync<StockCountDraft>();
            Assert.Equal(10m, Assert.Single(draft!.Lines).SystemQuantityAtBase);
            Assert.Equal(9m, Assert.Single(draft.Lines).PreCountQuantity);
        }
        await SendAcceptedAsync(
            inventory,
            $"/api/commerce/v1/stock-counts/{countId:D}/confirm",
            new ConfirmStockCountRequest(
                fixture.BusinessId,
                [new StockCountLineRequest(1, product.ProductId, 8m)]),
            $"count-{countId:N}");
        Assert.Equal(8m, await QuantityAsync(product.ProductId));

        var adjustmentId = Guid.NewGuid();
        await SendAcceptedAsync(
            inventory,
            "/api/commerce/v1/inventory-adjustments/confirm",
            new ConfirmInventoryAdjustmentRequest(
                adjustmentId,
                fixture.BusinessId,
                fixture.WarehouseId,
                occurredAt.AddMinutes(2),
                "FOUND_SURPLUS",
                null,
                "Unidad encontrada",
                [new InventoryAdjustmentLineRequest(1, product.ProductId, 1m, null)]),
            $"adjustment-{adjustmentId:N}");
        Assert.Equal(9m, await QuantityAsync(product.ProductId));

        var movements = await inventory.GetFromJsonAsync<InventoryMovementPage>(
            $"/api/commerce/v1/inventory/movements?productId={product.ProductId:D}&page=1&pageSize=20");
        Assert.NotNull(movements);
        Assert.Contains(movements.Items, item =>
            item.DocumentId == receiptId && item.QuantityChange == 10m);
        Assert.Contains(movements.Items, item =>
            item.DocumentId == countId && item.QuantityChange == -2m);
        Assert.Contains(movements.Items, item =>
            item.DocumentId == adjustmentId && item.QuantityChange == 1m);

        var history = await inventory.GetFromJsonAsync<InventoryOperationPage>(
            $"/api/commerce/v1/inventory/operations?warehouseId={fixture.WarehouseId:D}&page=1&pageSize=50");
        Assert.NotNull(history);
        Assert.Contains(history.Items, item => item.DocumentId == receiptId && item.Status == "Processed");
        Assert.Contains(history.Items, item => item.DocumentId == countId && item.Status == "Processed");
        Assert.Contains(history.Items, item => item.DocumentId == adjustmentId && item.Status == "Processed");

        var online = await catalog.GetFromJsonAsync<ProductPage>(
            $"/api/commerce/v1/products?barcode={Uri.EscapeDataString(barcode)}&page=1&pageSize=50");
        Assert.Contains(online!.Items, item => item.ProductId == product.ProductId);

        var sqlitePath = Path.Combine(
            Path.GetTempPath(), $"auraly-product-inventory-offline-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            var synchronizer = new PosCatalogSynchronizer(
                fixture.CreateClient(),
                local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            await synchronizer.SynchronizeAsync();
            var capturedOffline = await local.CaptureAsync(barcode);
            Assert.NotNull(capturedOffline);
            Assert.Equal(product.ProductId, capturedOffline.Product.ProductId);
            Assert.Equal(12_500m, capturedOffline.Product.UnitPrice);
            Assert.Equal(0L, await LocalScalarAsync(sqlitePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%Inventory%';"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }

    private async Task EnsureConfigurationAsync()
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@Tax)
              INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
              VALUES(@Tax,@Business,N'VAT19',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Channel)
              INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@Channel,@Business,N'POS',N'Punto de venta',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PosDevicePermissions WHERE DeviceId=@Device AND PermissionCode=@Sync)
              INSERT dbo.PosDevicePermissions(DeviceId,PermissionCode,IsGranted,GrantedAt)
              VALUES(@Device,@Sync,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@Business AND DocumentType=N'StockCount' AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@Business,NULL,N'StockCount',N'CTI',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@Business AND DocumentType=N'InventoryAdjustment' AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@Business,NULL,N'InventoryAdjustment',N'AJI',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Device", fixture.DeviceId);
        command.Parameters.AddWithValue("@Sync", CatalogPermissionCodes.Sync);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Tax", fixture.TaxProfileId);
        command.Parameters.AddWithValue("@Channel", fixture.PriceChannelId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<decimal> QuantityAsync(Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@Business AND WarehouseId=@Warehouse AND ProductId=@Product;",
            connection);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
        command.Parameters.AddWithValue("@Product", productId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private static async Task SendAcceptedAsync<T>(
        HttpClient client, string url, T request, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected Accepted, received {response.StatusCode}: {body}");
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
