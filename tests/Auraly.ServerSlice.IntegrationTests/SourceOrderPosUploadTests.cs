using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class SourceOrderPosUploadTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Accepted_sale_is_processed_even_when_another_work_session_is_open()
    {
        var request = fixture.CreateValidRequest(8_899) with
        {
            WorkSessionId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid()
        };
        using var client = fixture.CreateClient();
        using var upload = fixture.CreateUploadMessage(request);
        using var response = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT d.ProcessingStatus,j.Status,ws.Status,
                   COUNT(DISTINCT reportJob.SalesReportingJobId),
                   COUNT(DISTINCT movement.WorkSessionMovementId),
                   MAX(posting.Status)
            FROM dbo.SalesDocuments d
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=d.DocumentId
            INNER JOIN dbo.WorkSessions ws ON ws.WorkSessionId=d.WorkSessionId
            LEFT JOIN reporting.SalesReportingJobs reportJob
              ON reportJob.SourceDocumentId=d.DocumentId
            LEFT JOIN dbo.WorkSessionMovements movement
              ON movement.DocumentId=d.DocumentId AND movement.WorkSessionId=d.WorkSessionId
            LEFT JOIN dbo.AccountingPostingJobs posting
              ON posting.SourceDocumentId=d.DocumentId
            WHERE d.DocumentId=@DocumentId
            GROUP BY d.ProcessingStatus,j.Status,ws.Status;
            """;
        verify.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Completed", reader.GetString(0));
        Assert.Equal("Completed", reader.GetString(1));
        Assert.Equal("Open", reader.GetString(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(request.Payments.Count, reader.GetInt32(4));
        Assert.Equal("Posted", reader.GetString(5));
    }

    [Fact]
    public async Task Invoice_and_receipt_without_source_order_process_without_order_side_effects()
    {
        var receiptSeriesId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT dbo.DocumentSeries(
                  DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,
                  SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES(
                  @SeriesId,@BusinessId,@DeviceId,N'SalesReceipt',N'CVI',
                  N'04',8,1,99999999,1,1,SYSDATETIMEOFFSET());

                INSERT dbo.Products(
                  ProductId,TenantId,BusinessId,Source,Sku,Name,Currency,
                  ManageStock,IsActive,CreatedAt)
                VALUES(
                  @ProductId,@TenantId,@BusinessId,0,@Sku,N'Producto documentos sin pedido',N'COP',
                  1,1,SYSUTCDATETIME());

                INSERT dbo.InventoryBalances(
                  BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                  InventoryValue,LastProcessingSequence,UpdatedAt)
                VALUES(@BusinessId,@WarehouseId,@ProductId,10,5000,50000,1,SYSDATETIMEOFFSET());
                """;
            seed.Parameters.AddWithValue("@SeriesId", receiptSeriesId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@DeviceId", fixture.DeviceId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@ProductId", productId);
            seed.Parameters.AddWithValue("@Sku", $"NO-ORDER-{productId:N}");
            await seed.ExecuteNonQueryAsync();
        }

        var invoiceBase = fixture.CreateValidRequest(182);
        var invoice = invoiceBase with
        {
            Lines = invoiceBase.Lines
                .Select(line => line with { ProductId = productId })
                .ToArray(),
        };
        var receiptBase = fixture.CreateValidRequest(183);
        var receipt = receiptBase with
        {
            DocumentNumber = new PosSaleDocumentNumberContract(
                receiptSeriesId,
                PosSaleDocumentTypes.Receipt,
                "CVI",
                "04",
                183,
                8,
                "CVI04-00000183"),
            CommercialSnapshot = receiptBase.CommercialSnapshot with
            {
                DocumentType = PosSaleDocumentTypes.Receipt,
            },
            FiscalSnapshot = null,
            UblSnapshot = null,
            SourceOrderId = null,
            Lines = receiptBase.Lines
                .Select(line => line with { ProductId = productId })
                .ToArray(),
        };

        using var client = fixture.CreateClient();
        using (var upload = fixture.CreateUploadMessage(invoice))
        using (var response = await client.SendAsync(upload))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(result);
            Assert.Equal(PosSaleRemoteStatuses.FiscalVerified, result.Status);
        }

        using (var upload = fixture.CreateUploadMessage(receipt))
        using (var response = await client.SendAsync(upload))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(result);
            Assert.Equal(PosSaleRemoteStatuses.CommercialAccepted, result.Status);
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.SalesDocuments
               WHERE DocumentId IN (@InvoiceId,@ReceiptId) AND ProcessingStatus=N'Completed'),
              (SELECT COUNT(*) FROM dbo.SalesDocuments
               WHERE DocumentId=@InvoiceId AND DocumentType=N'SalesInvoice'),
              (SELECT COUNT(*) FROM dbo.SalesDocuments
               WHERE DocumentId=@ReceiptId AND DocumentType=N'SalesReceipt'),
              (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks
               WHERE DocumentId IN (@InvoiceId,@ReceiptId)),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId IN (@InvoiceId,@ReceiptId) AND MovementType=N'Sale'
                 AND WarehouseId=@SalesWarehouseId),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId IN (@InvoiceId,@ReceiptId)
                 AND MovementType IN (N'TransferOut',N'TransferIn'));
            """;
        verify.Parameters.AddWithValue("@InvoiceId", invoice.DocumentId);
        verify.Parameters.AddWithValue("@ReceiptId", receipt.DocumentId);
        verify.Parameters.AddWithValue("@SalesWarehouseId", fixture.WarehouseId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(2, reader.GetInt32(4));
        Assert.Equal(0, reader.GetInt32(5));

        using var returns = fixture.CreateAdminClient(SalesReturnPermissionCodes.Read);
        using var returnableResponse = await returns.GetAsync(
            $"/api/commerce/v1/sales-returns/sales?page=1&pageSize=20&search={Uri.EscapeDataString(receipt.DocumentNumber.FullNumber)}&withAvailableQuantity=true");
        Assert.True(returnableResponse.IsSuccessStatusCode,
            await returnableResponse.Content.ReadAsStringAsync());
        var returnable = await returnableResponse.Content.ReadFromJsonAsync<ReturnableSalePage>();
        var receiptResult = Assert.Single(returnable!.Items,
            item => item.DocumentId == receipt.DocumentId);
        Assert.Equal(string.Empty, receiptResult.FiscalNumber);
        Assert.Equal(string.Empty, receiptResult.Cufe);
    }

    [Fact]
    public async Task Pos_sale_links_pre_released_source_order_without_creating_line_level_transfers()
    {
        var orderId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var ordersWarehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT dbo.Warehouses(
                  WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                  IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
                VALUES(
                  @OrdersWarehouseId,@BusinessId,@OrdersWarehouseCode,N'Pedidos prueba',0,
                  1,0,0,0,1,SYSDATETIMEOFFSET());

                INSERT dbo.InventoryBalances(
                  BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                  InventoryValue,LastProcessingSequence,UpdatedAt)
                VALUES(@BusinessId,@OrdersWarehouseId,@ProductId,1,5000,5000,1,SYSDATETIMEOFFSET());

                INSERT dbo.Orders(
                  OrderId,BusinessId,Source,FulfillmentMode,Status,
                  CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
                  Subtotal,DiscountTotal,Total,CustomerConfirmed,
                  ExternalDocumentNumber,OrdersWarehouseId,ExternalStatus,CustomAttributesJson,CreatedAt)
                VALUES(
                  @OrderId,@BusinessId,0,0,2,N'Cliente POS',N'900100200',N'COP',
                  10000,0,10000,1,@OrderNumber,@OrdersWarehouseId,N'InventoryReleasedForInvoice',@Attributes,SYSUTCDATETIME());

                INSERT dbo.OrderItems(
                  OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
                  ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
                  DiscountAmount,LineTotal,CreatedAt)
                VALUES(
                  NEWID(),@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
                  N'Producto pedido POS',N'EA',1,10000,0,10000,SYSUTCDATETIME());

                INSERT dbo.OrderClaims(
                  OrderClaimId,BusinessId,WarehouseId,OrderId,WorkSessionId,DeviceId,UserId,
                  ClaimedAt,ExpiresAt,ReleasedAt)
                VALUES(
                  @ClaimId,@BusinessId,@WarehouseId,@OrderId,@WorkSessionId,@DeviceId,@UserId,
                  SYSDATETIMEOFFSET(),DATEADD(minute,10,SYSDATETIMEOFFSET()),NULL);
                """;
            seed.Parameters.AddWithValue("@OrderId", orderId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@WorkSessionId", fixture.WorkSessionId);
            seed.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            seed.Parameters.AddWithValue("@OrderNumber", $"PED-POS-{orderId:N}");
            seed.Parameters.AddWithValue("@ClaimId", claimId);
            seed.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
            seed.Parameters.AddWithValue("@OrdersWarehouseCode", $"PED-{ordersWarehouseId:N}"[..32]);
            seed.Parameters.AddWithValue("@Attributes", System.Text.Json.JsonSerializer.Serialize(new { ordersWarehouseId }));
            seed.Parameters.AddWithValue("@DeviceId", fixture.DeviceId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            await seed.ExecuteNonQueryAsync();
        }

        var sale = fixture.CreateValidRequest(184) with { SourceOrderId = orderId };
        using var client = fixture.CreateClient();
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await client.SendAsync(upload))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(PosSaleRemoteStatuses.FiscalVerified, receipt.Status);
        }
        await AssertProcessingCompletedAsync(sale.DocumentId);

        using (var duplicate = fixture.CreateUploadMessage(sale))
        using (var response = await client.SendAsync(duplicate))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(PosSaleRemoteStatuses.AlreadyProcessed, receipt.Status);
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks
               WHERE OrderId=@OrderId AND DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.OrderClaims
               WHERE OrderClaimId=@ClaimId AND ReleasedAt IS NOT NULL),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType=N'Sale'),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType=N'TransferOut' AND WarehouseId=@OrdersWarehouseId),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType IN (N'TransferIn',N'Sale') AND WarehouseId=@SalesWarehouseId);
            """;
        verify.Parameters.AddWithValue("@OrderId", orderId);
        verify.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
        verify.Parameters.AddWithValue("@ClaimId", claimId);
        verify.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        verify.Parameters.AddWithValue("@SalesWarehouseId", fixture.WarehouseId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
    }

    private async Task AssertProcessingCompletedAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.ProcessingStatus,j.Status,j.LastError
            FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=d.DocumentId AND j.DocumentType=d.DocumentType
            WHERE d.DocumentId=@DocumentId;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var documentStatus = reader.GetString(0);
        var jobStatus = reader.GetString(1);
        var error = reader.IsDBNull(2) ? null : reader.GetString(2);
        Assert.True(
            documentStatus == "Completed" && jobStatus == "Completed",
            $"Document status: {documentStatus}; job status: {jobStatus}; error: {error}");
    }
}
