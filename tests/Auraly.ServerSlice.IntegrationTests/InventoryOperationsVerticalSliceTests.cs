using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class InventoryOperationsVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Count_adjustment_transfer_and_conversion_preserve_order_quantity_value_and_idempotency()
    {
        var source = Guid.NewGuid();
        var outputOne = Guid.NewGuid();
        var outputTwo = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(source, outputOne, outputTwo, destination);
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Transfer,
            InventoryPermissionCodes.Convert);
        var occurred = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(-5));

        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            occurred, "INITIAL_BALANCE", null, "Saldo inicial",
            [new(1, source, 20m, 5m)]));
        Assert.Equal((20m, 5m, 100m), await BalanceAsync(fixture.WarehouseId, source));

        var countId = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/stock-counts/start",
                   new StartStockCountRequest(countId, fixture.BusinessId, fixture.WarehouseId,
                       occurred.AddMinutes(1), "PHYSICAL_COUNT", "Conteo ciego", [source])))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var draft = await response.Content.ReadFromJsonAsync<StockCountDraft>();
            Assert.NotNull(draft);
            Assert.Equal(20m, Assert.Single(draft.Lines).SystemQuantityAtBase);
        }

        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            occurred.AddMinutes(2), "FOUND_SURPLUS", null, null,
            [new(1, source, 5m, 5m)]));
        Assert.Equal(25m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);

        using (var message = new HttpRequestMessage(HttpMethod.Post, $"/api/commerce/v1/stock-counts/{countId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmStockCountRequest(fixture.BusinessId, [new(1, source, 18m)]))
        })
        {
            message.Headers.Add("Idempotency-Key", $"count-{countId:N}");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(23m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);
        Assert.Equal(-2m, await ScalarAsync<decimal>("SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'StockCountAdjustment'", countId));

        var transferId = Guid.NewGuid();
        var transfer = new ConfirmWarehouseTransferRequest(transferId, fixture.BusinessId,
            fixture.WarehouseId, destination, occurred.AddMinutes(3), "WAREHOUSE_TRANSFER", null,
            [new(1, source, 3m)]);
        var transferKey = $"transfer-{transferId:N}";
        var firstTransfer = await SendAsync<ConfirmWarehouseTransferRequest>(client,
            "/api/commerce/v1/warehouse-transfers/confirm", transfer, transferKey);
        Assert.False(firstTransfer.IdempotentReplay);
        var replay = await SendAsync<ConfirmWarehouseTransferRequest>(client,
            "/api/commerce/v1/warehouse-transfers/confirm", transfer, transferKey);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(20m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);
        Assert.Equal((3m, 5m, 15m), await BalanceAsync(destination, source));
        Assert.Equal(2, await CountAsync("InventoryMovements", transferId));

        var conversionId = Guid.NewGuid();
        await SendAsync<ConfirmProductConversionRequest>(client,
            "/api/commerce/v1/product-conversions/confirm",
            new(conversionId, fixture.BusinessId, fixture.WarehouseId, occurred.AddMinutes(4),
                "SPLIT", "PRESENTATION_CHANGE", null, "Conversión E2E",
                [new(1,"INPUT",source,4m,null), new(2,"OUTPUT",outputOne,2m,60m), new(3,"OUTPUT",outputTwo,1m,40m)]),
            $"conversion-{conversionId:N}");
        Assert.Equal((16m, 5m, 80m), await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal((2m, 6m, 12m), await BalanceAsync(fixture.WarehouseId, outputOne));
        Assert.Equal((1m, 8m, 8m), await BalanceAsync(fixture.WarehouseId, outputTwo));
        Assert.Equal(3, await CountAsync("InventoryMovements", conversionId));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT TotalValueChange FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", conversionId));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id", conversionId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", conversionId));
        Assert.Equal("Completed", await JobStatusAsync(conversionId));
    }

    [Fact]
    public async Task Stock_count_with_two_changed_products_creates_two_kardex_movements()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await SeedAsync(first, second, Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust);
        var occurred = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.FromHours(-5));

        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, occurred,
            "INITIAL_BALANCE", null, "Base para conteo de dos líneas",
            [new(1, first, 10m, 5m), new(2, second, 8m, 5m)]));

        var countId = Guid.NewGuid();
        using (var start = await client.PostAsJsonAsync(
            "/api/commerce/v1/stock-counts/start",
            new StartStockCountRequest(
                countId, fixture.BusinessId, fixture.WarehouseId,
                occurred.AddMinutes(1), "PHYSICAL_COUNT", "Conteo de dos líneas",
                [first, second])))
        {
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            var draft = await start.Content.ReadFromJsonAsync<StockCountDraft>();
            Assert.NotNull(draft);
            Assert.Equal(2, draft.Lines.Count);
        }

        using (var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/commerce/v1/stock-counts/{countId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmStockCountRequest(
                fixture.BusinessId,
                [new(1, first, 7m), new(2, second, 12m)]))
        })
        {
            request.Headers.Add("Idempotency-Key", $"count-two-{countId:N}");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(7m, (await BalanceAsync(fixture.WarehouseId, first)).Quantity);
        Assert.Equal(12m, (await BalanceAsync(fixture.WarehouseId, second)).Quantity);
        Assert.Equal(2, await CountAsync("InventoryMovements", countId));
        Assert.Equal(-3m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND ProductId='" + first + "'", countId));
        Assert.Equal(4m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND ProductId='" + second + "'", countId));
    }
    [Fact]
    public async Task Damage_updates_inventory_once_and_queries_respect_cost_permission()
    {
        var product = Guid.NewGuid();
        await SeedAsync(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Damage, InventoryPermissionCodes.Read, InventoryPermissionCodes.ReadCosts);
        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null, [new(1, product, 10m, 5m)]));
        var damageId = Guid.NewGuid();
        var request = new ConfirmInventoryDamageRequest(damageId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow, "DAMAGE", null, "Empaque destruido", [new(1, product, 3m)]);
        var key = $"damage-{damageId:N}";
        var accepted = await SendAsync(client, "/api/commerce/v1/inventory-damages/confirm", request, key);
        var replay = await SendAsync(client, "/api/commerce/v1/inventory-damages/confirm", request, key);
        Assert.False(accepted.IdempotentReplay); Assert.True(replay.IdempotentReplay);
        Assert.Equal((7m,5m,35m), await BalanceAsync(fixture.WarehouseId, product));
        Assert.Equal(-3m, await ScalarAsync<decimal>("SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'InventoryDamage'", damageId));
        Assert.Equal(1, await CountAsync("InventoryMovements", damageId)); Assert.Equal(1, await CountAsync("ServerOutboxMessages", damageId));
        var balances = await client.GetFromJsonAsync<InventoryBalancePage>($"/api/commerce/v1/inventory/balances?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var row = Assert.Single(balances!.Items.Where(x => x.ProductId == product)); Assert.Equal(5m,row.AverageUnitCost); Assert.Equal(35m,row.InventoryValue);
        var products = await client.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var productRow = Assert.Single(products!.Items.Where(x => x.ProductId == product)); Assert.Equal(7m, productRow.QuantityOnHand); Assert.Equal(5m, productRow.AverageUnitCost);
        foreach (var search in new[] { $"I-{product:N}", $"REF-{product:N}", $"BAR-{product:N}" })
        {
            var result = await client.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search={search}&page=1&pageSize=20");
            Assert.Contains(result!.Items, item => item.ProductId == product);
        }
        var movements = await client.GetFromJsonAsync<InventoryMovementPage>($"/api/commerce/v1/inventory/movements?productId={product:D}&page=1&pageSize=20");
        Assert.Contains(movements!.Items,x=>x.DocumentId==damageId&&x.MovementType=="InventoryDamage");
        using var restricted = fixture.CreateAdminClient(InventoryPermissionCodes.Read);
        var hidden = await restricted.GetFromJsonAsync<InventoryBalancePage>($"/api/commerce/v1/inventory/balances?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var hiddenRow = Assert.Single(hidden!.Items.Where(x => x.ProductId == product)); Assert.Null(hiddenRow.AverageUnitCost); Assert.Null(hiddenRow.InventoryValue);
        var hiddenProducts = await restricted.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        Assert.Null(Assert.Single(hiddenProducts!.Items.Where(x => x.ProductId == product)).AverageUnitCost);
    }
    [Fact]
    public async Task Inventory_endpoints_enforce_permission_and_transaction_rolls_back_an_impossible_conversion()
    {
        var source = Guid.NewGuid(); var output = Guid.NewGuid(); var destination = Guid.NewGuid();
        await SeedAsync(source, output, Guid.NewGuid(), destination);
        var request = new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null,
            [new(1, source, 1m, 2m)]);
        using (var deniedClient = fixture.CreateAdminClient())
        using (var deniedMessage = CreateMessage("/api/commerce/v1/inventory-adjustments/confirm", request, Guid.NewGuid().ToString("N")))
        using (var denied = await deniedClient.SendAsync(deniedMessage))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Convert);
        await ConfirmAdjustmentAsync(client, request);
        var before = await BalanceAsync(fixture.WarehouseId, source);
        var conversionId = Guid.NewGuid();
        await SendAsync<ConfirmProductConversionRequest>(client,
            "/api/commerce/v1/product-conversions/confirm",
            new(conversionId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
                "SPLIT", "PRESENTATION_CHANGE", null, null,
                [new(1,"INPUT",source,99m,null),new(2,"OUTPUT",output,1m,null)]),
            $"impossible-{conversionId:N}");
        Assert.Equal(before, await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal(0, await CountAsync("InventoryMovements", conversionId));
        Assert.Equal(0, await CountAsync("ServerOutboxMessages", conversionId));
        Assert.Equal("RetryScheduled", await JobStatusAsync(conversionId));
        await ExhaustRetriesAsync(conversionId);
        Assert.Equal("DeadLettered", await JobStatusAsync(conversionId));
    }

    private async Task SeedAsync(Guid first, Guid second, Guid third, Guid destination)
    {
        const string sql = """
            INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsActive,CreatedAt)
            VALUES(@Destination,@BusinessId,@WarehouseCode,N'Bodega destino',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(
                TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'IVA de prueba',19,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,BusinessId,ProductCode,Reference,BaseUnitCode,TaxProfileId,Source,Sku,Name,UnitPrice,Currency,ManageStock,IsActive,CreatedAt)
            VALUES(@First,@BusinessId,@FirstSku,@FirstReference,N'EA',@TaxProfileId,0,@FirstSku,N'Insumo',0,N'COP',1,1,SYSUTCDATETIME()),
                  (@Second,@BusinessId,NULL,NULL,N'EA',@TaxProfileId,0,@SecondSku,N'Salida uno',0,N'COP',1,1,SYSUTCDATETIME()),
                  (@Third,@BusinessId,NULL,NULL,N'EA',@TaxProfileId,0,@ThirdSku,N'Salida dos',0,N'COP',1,1,SYSUTCDATETIME());
            INSERT dbo.ProductBarcodes(ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@First,@FirstBarcode,1,1,SYSUTCDATETIME());
            IF NOT EXISTS(
                SELECT 1 FROM dbo.DocumentSeries
                WHERE BusinessId=@BusinessId AND DocumentType=N'StockCount'
                  AND Prefix=N'CTI' AND SeriesCode=@Series)
            BEGIN
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,NULL,N'StockCount',N'CTI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'InventoryAdjustment',N'AJI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'WarehouseTransfer',N'TRB',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'ProductConversion',N'CNV',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'Damage' AND IsActive=1)
                  INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'Damage',N'AVE',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET());
            END;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Destination",destination); command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId); command.Parameters.AddWithValue("@TaxProfileId",Guid.NewGuid()); command.Parameters.AddWithValue("@TaxCode",$"IVA-{first:N}"[..32]);
        command.Parameters.AddWithValue("@WarehouseCode",$"W-{destination:N}"[..18]); command.Parameters.AddWithValue("@First",first); command.Parameters.AddWithValue("@Second",second); command.Parameters.AddWithValue("@Third",third);
        command.Parameters.AddWithValue("@FirstSku",$"I-{first:N}"); command.Parameters.AddWithValue("@FirstReference",$"REF-{first:N}"); command.Parameters.AddWithValue("@FirstBarcode",$"BAR-{first:N}"); command.Parameters.AddWithValue("@SecondSku",$"O-{second:N}"); command.Parameters.AddWithValue("@ThirdSku",$"O-{third:N}"); command.Parameters.AddWithValue("@Series","00");
        await command.ExecuteNonQueryAsync();
    }

    private async Task ConfirmAdjustmentAsync(HttpClient client, ConfirmInventoryAdjustmentRequest request) =>
        _ = await SendAsync<ConfirmInventoryAdjustmentRequest>(client, "/api/commerce/v1/inventory-adjustments/confirm", request, $"adjust-{request.DocumentId:N}");

    private static async Task<InventoryOperationAcceptance> SendAsync<T>(HttpClient client, string url, T request, string key)
    {
        using var message = CreateMessage(url, request, key); using var response = await client.SendAsync(message);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected Accepted, received {response.StatusCode}: {responseBody}");
        return await response.Content.ReadFromJsonAsync<InventoryOperationAcceptance>() ?? throw new InvalidOperationException("Acceptance missing.");
    }
    private static HttpRequestMessage CreateMessage<T>(string url,T request,string key){var message=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(request)};message.Headers.Add("Idempotency-Key",key);return message;}
    private async Task ExhaustRetriesAsync(Guid documentId)
    {
        var movementId = await ScalarAsync<Guid>("SELECT JobId FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id", documentId);
        var signal = new Auraly.Application.DocumentProcessing.DocumentProcessingSignal(movementId, fixture.BusinessId, documentId, InventoryDocumentTypes.Conversion);
        for (var attempt = 2; attempt <= 5; attempt++)
        {
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("UPDATE dbo.DocumentProcessingJobs SET AvailableAt=DATEADD(second,-1,SYSDATETIMEOFFSET()) WHERE DocumentId=@Id;", connection);
                command.Parameters.AddWithValue("@Id", documentId);
                await command.ExecuteNonQueryAsync();
            }
            await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(fixture.Services);
            var worker = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Auraly.Application.DocumentProcessing.DocumentProcessingWorker>(scope.ServiceProvider);
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessOneAsync(signal));
        }
    }
    private async Task<(decimal Quantity,decimal Average,decimal Value)> BalanceAsync(Guid warehouse,Guid product)
    {await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand("SELECT QuantityOnHand,AverageUnitCost,InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId",connection);command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@WarehouseId",warehouse);command.Parameters.AddWithValue("@ProductId",product);await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync())return(0,0,0);return(reader.GetDecimal(0),reader.GetDecimal(1),reader.GetDecimal(2));}
    private async Task<T> ScalarAsync<T>(string sql,Guid id){await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!,typeof(T));}
    private async Task<int> CountAsync(string table,Guid id){Assert.Contains(table,new[]{"InventoryMovements","ServerOutboxMessages"});return await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{table}] WHERE DocumentId=@Id",id);}
    private Task<string> JobStatusAsync(Guid id)=>ScalarAsync<string>("SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id",id);
}
