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
            occurred.AddMinutes(2), "FOUND_STOCK", null, null,
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
            fixture.WarehouseId, destination, occurred.AddMinutes(3), "REPLENISHMENT", null,
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
                "SPLIT", "CHANGE_PRESENTATION", null, "Conversión E2E",
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
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendAsync<ConfirmProductConversionRequest>(client, "/api/commerce/v1/product-conversions/confirm",
                new(conversionId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
                    "SPLIT", "CHANGE_PRESENTATION", null, null,
                    [new(1,"INPUT",source,99m,null),new(2,"OUTPUT",output,1m,null)]),
                $"impossible-{conversionId:N}"));
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
            INSERT dbo.Products(ProductId,BusinessId,Source,Sku,Name,UnitPrice,Currency,ManageStock,IsActive,CreatedAt)
            VALUES(@First,@BusinessId,0,@FirstSku,N'Insumo',0,N'COP',1,1,SYSUTCDATETIME()),
                  (@Second,@BusinessId,0,@SecondSku,N'Salida uno',0,N'COP',1,1,SYSUTCDATETIME()),
                  (@Third,@BusinessId,0,@ThirdSku,N'Salida dos',0,N'COP',1,1,SYSUTCDATETIME());
            INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,RegisterId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,NULL,N'StockCount',N'CTI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                  (NEWID(),@BusinessId,NULL,N'InventoryAdjustment',N'AJI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                  (NEWID(),@BusinessId,NULL,N'WarehouseTransfer',N'TRB',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                  (NEWID(),@BusinessId,NULL,N'ProductConversion',N'CNV',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Destination",destination); command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseCode",$"W-{destination:N}"[..18]); command.Parameters.AddWithValue("@First",first); command.Parameters.AddWithValue("@Second",second); command.Parameters.AddWithValue("@Third",third);
        command.Parameters.AddWithValue("@FirstSku",$"I-{first:N}"); command.Parameters.AddWithValue("@SecondSku",$"O-{second:N}"); command.Parameters.AddWithValue("@ThirdSku",$"O-{third:N}"); command.Parameters.AddWithValue("@Series",destination.ToString("N")[..8].ToUpperInvariant());
        await command.ExecuteNonQueryAsync();
    }

    private async Task ConfirmAdjustmentAsync(HttpClient client, ConfirmInventoryAdjustmentRequest request) =>
        _ = await SendAsync<ConfirmInventoryAdjustmentRequest>(client, "/api/commerce/v1/inventory-adjustments/confirm", request, $"adjust-{request.DocumentId:N}");

    private static async Task<InventoryOperationAcceptance> SendAsync<T>(HttpClient client, string url, T request, string key)
    {
        using var message = CreateMessage(url, request, key); using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
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
