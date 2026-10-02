using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class InventoryCostCorrectionRecoveryTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Cost_correction_revalues_sellable_pool_and_damage_without_changing_quantities()
    {
        var productId = Guid.NewGuid();
        await SeedAsync(productId);
        var damagedWarehouseId = await WarehouseAsync("AVE");
        var ordersWarehouseId = await WarehouseAsync("PED");
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Adjust, InventoryPermissionCodes.ReadCosts);
        var occurredAt = new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.FromHours(-5));

        var correctionId = Guid.NewGuid();
        await ConfirmAsync(client, new ConfirmInventoryCostCorrectionRequest(
            correctionId, fixture.BusinessId, fixture.WarehouseId, occurredAt,
            "MANUAL_ADJUSTMENT", null, "Restaurar costo histórico verificado en el kardex",
            productId, 2560.016m, 28.5m, 0m));

        Assert.Equal((20.5m, 2560.016m, 52480.3280m),
            await BalanceAsync(fixture.WarehouseId, productId));
        Assert.Equal((8m, 2560.016m, 20480.1280m),
            await BalanceAsync(ordersWarehouseId, productId));
        Assert.Equal((5.5m, 1000m, 5500m),
            await BalanceAsync(damagedWarehouseId, productId));
        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId", correctionId));
        Assert.Equal(72960.4560m, await ScalarAsync<decimal>(
            "SELECT ValueChange FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId", correctionId));

        var damageCorrectionId = Guid.NewGuid();
        await ConfirmAsync(client, new ConfirmInventoryCostCorrectionRequest(
            damageCorrectionId, fixture.BusinessId, damagedWarehouseId,
            occurredAt.AddMinutes(1), "MANUAL_ADJUSTMENT", null,
            "Eliminar valor residual de avería previamente descargada",
            productId, 0m, 5.5m, 5500m));
        Assert.Equal((5.5m, 0m, 0m), await BalanceAsync(damagedWarehouseId, productId));
        Assert.Equal((20.5m, 2560.016m, 52480.3280m),
            await BalanceAsync(fixture.WarehouseId, productId));
        Assert.Equal(-5500m, await ScalarAsync<decimal>(
            "SELECT ValueChange FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId", damageCorrectionId));
    }

    [Fact]
    public async Task Audited_recovery_completes_the_original_fiscal_sale_once_after_cost_is_fixed()
    {
        var request = fixture.CreateValidRequest(7123);
        var poolQuantity = await ClearFixtureProductCostAsync();
        using (var deviceClient = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(request))
        using (var response = await deviceClient.SendAsync(upload))
            Assert.True(response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());

        Assert.Equal("RetryScheduled", await ScalarAsync<string>(
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoice'",
            request.DocumentId));
        await CompleteFailedAttemptForTestAsync(request.DocumentId);

        using var admin = fixture.CreateAdminClient(
            InventoryPermissionCodes.Adjust, InventoryPermissionCodes.ReadCosts,
            FiscalPermissionCodes.Correct);
        await ConfirmAsync(admin, new ConfirmInventoryCostCorrectionRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            DateTimeOffset.UtcNow, "MANUAL_ADJUSTMENT", null,
            "Restaurar costo del kardex antes de recuperar factura verificada",
            fixture.ProductId, 6000m, poolQuantity, 0m));

        using (var response = await admin.PostAsJsonAsync(
                   $"/api/commerce/v1/fiscal/documents/{request.DocumentId:D}/recover-dead-letter",
                   new { reason = "Corregido el costo cero que bloqueó esta factura" }))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Equal("DeadLettered", await ScalarAsync<string>(
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoice'",
            request.DocumentId));
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoiceRecovery'",
            request.DocumentId));
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT ProcessingStatus FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId",
            request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@DocumentId", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId AND MovementType=N'Sale'",
            request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@DocumentId AND SourceDocumentProcessingJobId=(SELECT JobId FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoiceRecovery')",
            request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ServerOutboxMessages WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoiceRecovery' AND ProcessedAt IS NOT NULL",
            request.DocumentId));

        using (var response = await admin.PostAsJsonAsync(
                   $"/api/commerce/v1/fiscal/documents/{request.DocumentId:D}/recover-dead-letter",
                   new { reason = "Reintento de respuesta perdida" }))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId AND MovementType=N'Sale'",
            request.DocumentId));
    }

    private async Task<decimal> ClearFixtureProductCostAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.InventoryBalances
              WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId)
              INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,
                AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,10,0,0,
                COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),
                SYSDATETIMEOFFSET());
            DECLARE @PoolQuantity decimal(19,6);
            SELECT @PoolQuantity=SUM(balance.QuantityOnHand)
            FROM dbo.InventoryBalances balance
            JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=balance.WarehouseId
            WHERE balance.BusinessId=@BusinessId AND balance.ProductId=@ProductId
              AND NOT(warehouse.IsSystem=1 AND warehouse.Code=N'AVE');
            UPDATE dbo.InventoryBalances SET AverageUnitCost=0,InventoryValue=0
            WHERE BusinessId=@BusinessId AND ProductId=@ProductId;
            SELECT @PoolQuantity;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return (decimal)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException());
    }

    private async Task CompleteFailedAttemptForTestAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @Sequence bigint;
            SELECT @Sequence=ProcessingSequence FROM dbo.DocumentProcessingJobs
            WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoice' AND Status=N'RetryScheduled'
              AND LastError LIKE N'%valorización contable positiva%';
            IF @Sequence IS NULL THROW 51000,N'The sale did not fail for zero inventory cost.',1;
            UPDATE dbo.DocumentProcessingJobs SET Status=N'DeadLettered',AttemptCount=5
            WHERE DocumentId=@DocumentId AND DocumentType=N'SalesInvoice';
            UPDATE dbo.BusinessProcessingCursors
            SET LastCompletedSequence=@Sequence
            WHERE BusinessId=@BusinessId AND LastCompletedSequence=@Sequence-1;
            IF @@ROWCOUNT<>1 THROW 51000,N'The test cursor did not advance.',1;
            """, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'AVE')
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'AVE',N'Averías',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'PED')
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'PED',N'Pedidos',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            SELECT NEWID(),@BusinessId,NULL,seed.DocumentType,seed.Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET()
            FROM (VALUES (N'StockCount',N'CTI'),(N'InventoryAdjustment',N'AJI'),
                         (N'WarehouseTransfer',N'TRB'),(N'ProductConversion',N'CNV'),
                         (N'Damage',N'AVE')) seed(DocumentType,Prefix)
            WHERE NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries existing
              WHERE existing.BusinessId=@BusinessId AND existing.DocumentType=seed.DocumentType
                AND existing.Prefix=seed.Prefix AND existing.SeriesCode=N'00');
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@TenantId,@TaxCode,N'IVA de prueba',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,Reference,BaseUnitCode,TaxProfileId,Source,Sku,Name,Currency,ManageStock,IsActive,CreatedAt)
            VALUES(@ProductId,@TenantId,@Code,@Code,N'KG',@TaxProfileId,0,@Code,N'Remolacha de prueba',N'COP',1,1,SYSUTCDATETIME());
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            SELECT @BusinessId,w.WarehouseId,@ProductId,
                   CASE WHEN w.WarehouseId=@SalesWarehouseId THEN 20.5
                        WHEN w.Code=N'PED' THEN 8 ELSE 5.5 END,
                   CASE WHEN w.Code=N'AVE' THEN 1000 ELSE 0 END,
                   CASE WHEN w.Code=N'AVE' THEN 5500 ELSE 0 END,
                   COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),
                   SYSDATETIMEOFFSET()
            FROM dbo.Warehouses w
            WHERE w.BusinessId=@BusinessId AND (w.WarehouseId=@SalesWarehouseId OR w.Code IN(N'PED',N'AVE'));
            """, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@SalesWarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@Code", $"COST-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@TaxProfileId", Guid.NewGuid());
        command.Parameters.AddWithValue("@TaxCode", $"VAT-{productId:N}"[..32]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ConfirmAsync(HttpClient client, ConfirmInventoryCostCorrectionRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/inventory-adjustments/correct-cost")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"cost-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> WarehouseAsync(string code)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT WarehouseId FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=@Code", connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Code", code);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException());
    }

    private async Task<(decimal Quantity, decimal Average, decimal Value)> BalanceAsync(
        Guid warehouseId, Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT QuantityOnHand,AverageUnitCost,InventoryValue
            FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException());
    }
}
