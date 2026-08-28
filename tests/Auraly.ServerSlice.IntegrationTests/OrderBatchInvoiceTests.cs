using System.Net.Http.Json;
using Auraly.Application.Orders;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OrderBatchInvoiceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Accepted_order_finishes_processing_after_its_work_session_closes()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        var command = new InvoiceOrdersRequest(
            workSessionId,
            fixture.WarehouseId,
            userId,
            [orderId],
            "Cash",
            null);

        fixture.PauseDocumentProcessing();
        try
        {
            var invoice = await InvoiceAsync(
                client,
                command,
                $"closed-session-{Guid.NewGuid():N}");
            var invoiceResult = Assert.Single(invoice.Results);
            Assert.True(
                invoiceResult.DocumentId.HasValue,
                $"Order invoicing failed before document reception: {invoiceResult.Error ?? "no detail"}.");
            var documentId = invoiceResult.DocumentId.Value;
            var queuedSignals = fixture.DrainDocumentSignals();
            if (queuedSignals.Count != 1)
            {
                await using var diagnosticConnection = new SqlConnection(fixture.ConnectionString);
                await diagnosticConnection.OpenAsync();
                await using var diagnostic = diagnosticConnection.CreateCommand();
                diagnostic.CommandText = """
                    SELECT d.ProcessingStatus,d.FiscalStatus,s.ConflictReason
                    FROM dbo.SalesDocuments d
                    LEFT JOIN dbo.FiscalSnapshots s ON s.DocumentId=d.DocumentId
                    WHERE d.DocumentId=@DocumentId;
                    """;
                diagnostic.Parameters.AddWithValue("@DocumentId", documentId);
                await using var diagnosticReader = await diagnostic.ExecuteReaderAsync();
                Assert.True(await diagnosticReader.ReadAsync());
                Assert.Fail(
                    $"Expected one processing signal but found {queuedSignals.Count}. " +
                    $"Document status: {diagnosticReader.GetString(0)}; " +
                    $"fiscal status: {(diagnosticReader.IsDBNull(1) ? "none" : diagnosticReader.GetString(1))}; " +
                    $"conflict: {(diagnosticReader.IsDBNull(2) ? "none" : diagnosticReader.GetString(2))}.");
            }
            var signal = Assert.Single(queuedSignals);

            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var close = connection.CreateCommand();
                close.CommandText = """
                    UPDATE dbo.WorkSessions
                    SET Status=N'Closed',ClosedAt=SYSDATETIMEOFFSET(),
                        LastActivityAt=SYSDATETIMEOFFSET()
                    WHERE WorkSessionId=@WorkSessionId;
                    """;
                close.Parameters.AddWithValue("@WorkSessionId", workSessionId);
                Assert.Equal(1, await close.ExecuteNonQueryAsync());
            }

            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);

            await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var verify = verifyConnection.CreateCommand();
            verify.CommandText = """
                SELECT d.ProcessingStatus,j.Status,w.Status,
                       (SELECT COUNT_BIG(1) FROM dbo.SalesDocuments
                        WHERE DocumentId=@DocumentId)
                FROM dbo.SalesDocuments d
                JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=d.DocumentId
                JOIN dbo.WorkSessions w ON w.WorkSessionId=d.WorkSessionId
                WHERE d.DocumentId=@DocumentId;
                """;
            verify.Parameters.AddWithValue("@DocumentId", documentId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Completed", reader.GetString(0));
            Assert.Equal("Completed", reader.GetString(1));
            Assert.Equal("Closed", reader.GetString(2));
            Assert.Equal(1, reader.GetInt64(3));
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }
    }

    [Fact]
    public async Task Selected_orders_create_independent_invoices_exactly_once()
    {
        var userId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, firstOrderId, secondOrderId);

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        var command = new InvoiceOrdersRequest(
            workSessionId,
            fixture.WarehouseId,
            userId,
            [firstOrderId, secondOrderId],
            "Cash",
            null);
        var idempotencyKey = $"orders-{Guid.NewGuid():N}";

        // Production brokers process after checkout has committed. Reproduce that
        // ordering so the durable order/document link already exists when the
        // canonical sales engine consumes each document.
        fixture.PauseDocumentProcessing();
        InvoiceOrdersResponse first;
        try
        {
            first = await InvoiceAsync(client, command, idempotencyKey);
            var queued = fixture.DrainDocumentSignals();
            Assert.Equal(2, queued.Count);
            fixture.ResumeDocumentProcessing();
            foreach (var signal in queued)
                await fixture.DocumentSignals.PublishAsync(signal);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }
        Assert.Equal("Completed", first.Status);
        Assert.Equal(2, first.CompletedCount);
        Assert.Equal(0, first.FailedCount);
        Assert.False(first.IsReplay);
        Assert.Equal(2, first.Results.Count);
        Assert.All(first.Results, result =>
        {
            Assert.Equal("Invoiced", result.Status);
            Assert.NotNull(result.DocumentId);
            Assert.NotNull(result.DocumentNumber);
        });
        Assert.Equal(
            2,
            first.Results.Select(result => result.DocumentId).Distinct().Count());

        var replay = await InvoiceAsync(client, command, idempotencyKey);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(
            first.Results.Select(result => result.DocumentId),
            replay.Results.Select(result => result.DocumentId));

        var invoicedPage = await client.GetFromJsonAsync<OrderPage>(
            "/api/commerce/v1/orders?page=1&pageSize=20&status=Invoiced");
        Assert.NotNull(invoicedPage);
        Assert.Contains(invoicedPage.Items, item => item.OrderId == firstOrderId);
        Assert.Contains(invoicedPage.Items, item => item.OrderId == secondOrderId);
        Assert.All(
            invoicedPage.Items.Where(item => item.OrderId == firstOrderId || item.OrderId == secondOrderId),
            item => Assert.Equal("Invoiced", item.Status));

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId)),
              (SELECT COUNT(DISTINCT DocumentId) FROM dbo.OrderInvoiceLinks
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId)),
              (SELECT COUNT(*) FROM dbo.OrderInvoiceBatchReceipts
               WHERE OperationId=@OperationId AND Status=N'Completed'),
              (SELECT COUNT(*) FROM dbo.SalesDocuments
               WHERE DocumentId IN (
                 SELECT DocumentId FROM dbo.OrderInvoiceLinks
                 WHERE OrderId IN (@FirstOrderId,@SecondOrderId))
                 AND ProcessingStatus=N'Completed'),
              (SELECT COUNT(*) FROM dbo.Orders
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId) AND TaxTotal<>0),
              (SELECT COUNT(*) FROM dbo.OrderItems
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId) AND TaxAmount<>0);
            """;
        verify.Parameters.AddWithValue("@FirstOrderId", firstOrderId);
        verify.Parameters.AddWithValue("@SecondOrderId", secondOrderId);
        verify.Parameters.AddWithValue("@OperationId", first.OperationId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(2, reader.GetInt32(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.Equal(0, reader.GetInt32(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_order_emission_retries_in_the_next_processable_sequence(
        bool cursorAlreadyAdvanced)
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        var command = new InvoiceOrdersRequest(
            workSessionId,
            fixture.WarehouseId,
            userId,
            [orderId],
            "Cash",
            null);

        fixture.PauseDocumentProcessing();
        try
        {
            var invoice = await InvoiceAsync(
                client,
                command,
                $"retry-source-{Guid.NewGuid():N}");
            var originalSignal = Assert.Single(fixture.DrainDocumentSignals());
            var documentId = Assert.Single(invoice.Results).DocumentId!.Value;
            Assert.Equal(documentId, originalSignal.DocumentId);

            long originalSequence;
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var fail = connection.CreateCommand();
                fail.CommandText = """
                    UPDATE dbo.DocumentProcessingJobs
                    SET Status=CASE WHEN @CursorAlreadyAdvanced=1 THEN N'Pending' ELSE N'DeadLettered' END,
                        AttemptCount=5,LastError=N'forced regression failure'
                    WHERE DocumentId=@DocumentId;
                    IF @CursorAlreadyAdvanced=1
                      UPDATE dbo.BusinessProcessingCursors
                      SET LastCompletedSequence=(
                            SELECT ProcessingSequence FROM dbo.DocumentProcessingJobs
                            WHERE DocumentId=@DocumentId),
                          LastAssignedSequence=CASE
                            WHEN LastAssignedSequence<(
                              SELECT ProcessingSequence FROM dbo.DocumentProcessingJobs
                              WHERE DocumentId=@DocumentId)
                            THEN (SELECT ProcessingSequence FROM dbo.DocumentProcessingJobs
                                  WHERE DocumentId=@DocumentId)
                            ELSE LastAssignedSequence END
                      WHERE BusinessId=@BusinessId;
                    SELECT ProcessingSequence FROM dbo.DocumentProcessingJobs
                    WHERE DocumentId=@DocumentId;
                    """;
                fail.Parameters.AddWithValue("@DocumentId", documentId);
                fail.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                fail.Parameters.AddWithValue("@CursorAlreadyAdvanced", cursorAlreadyAdvanced);
                originalSequence = Convert.ToInt64(await fail.ExecuteScalarAsync());
            }

            using var response = await client.PostAsync(
                $"/api/commerce/v1/orders/{orderId:D}/emission/retry",
                content: null);
            response.EnsureSuccessStatusCode();
            var retry = await response.Content.ReadFromJsonAsync<OrderEmissionRetry>();
            Assert.NotNull(retry);
            Assert.Equal(documentId, retry.DocumentId);

            var retrySignal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(originalSignal.MovementId, retrySignal.MovementId);
            Assert.Equal(originalSignal.DocumentId, retrySignal.DocumentId);

            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var verify = connection.CreateCommand();
                verify.CommandText = """
                    SELECT ProcessingSequence,Status,AttemptCount,LastError
                    FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId;
                    """;
                verify.Parameters.AddWithValue("@DocumentId", documentId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(
                    cursorAlreadyAdvanced ? originalSequence + 1 : originalSequence,
                    reader.GetInt64(0));
                Assert.Equal("Pending", reader.GetString(1));
                Assert.Equal(0, reader.GetInt32(2));
                Assert.True(reader.IsDBNull(3));
            }

            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(retrySignal);

            var completed = await client.GetFromJsonAsync<OrderDetail>(
                $"/api/commerce/v1/orders/{orderId:D}");
            Assert.NotNull(completed);
            Assert.Equal("Invoiced", completed.Status);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }
    }

    [Fact]
    public async Task Invoice_batch_requires_explicit_invoice_permission()
    {
        var userId = Guid.NewGuid();
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/commerce/v1/orders/invoice")
        {
            Content = JsonContent.Create(new InvoiceOrdersRequest(
                Guid.NewGuid(),
                fixture.WarehouseId,
                userId,
                [Guid.NewGuid()],
                "Cash",
                null))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    private static async Task<InvoiceOrdersResponse> InvoiceAsync(
        HttpClient client,
        InvoiceOrdersRequest command,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/commerce/v1/orders/invoice")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Order invoicing returned {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<InvoiceOrdersResponse>()
            ?? throw new InvalidOperationException("Empty batch response.");
    }

    private async Task SeedAsync(
        Guid userId,
        Guid workSessionId,
        Guid firstOrderId,
        Guid secondOrderId)
    {
        var ordersWarehouseId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Lote',N'Pedidos',1,SYSDATETIMEOFFSET());

            INSERT dbo.WorkSessions(
              WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
              OpenedAt,LastActivityAt,Status)
            VALUES(
              @WorkSessionId,@BusinessId,@WarehouseId,@UserId,NULL,
              SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),N'Open');

            INSERT dbo.Warehouses(
              WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
              IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
            VALUES(
              @OrdersWarehouseId,@BusinessId,@OrdersWarehouseCode,N'Pedidos lote',0,
              1,0,0,0,1,SYSDATETIMEOFFSET());

            INSERT dbo.InventoryBalances(
              BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(@BusinessId,@OrdersWarehouseId,@ProductId,3,5000,15000,1,SYSDATETIMEOFFSET());

            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=100,AverageUnitCost=5000,InventoryValue=500000,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,OrdersWarehouseId,CreatedAt,CustomAttributesJson)
            VALUES
              (@FirstOrderId,@BusinessId,0,0,2,N'Cliente uno',N'1001',N'COP',
               10000,0,10000,1,N'PED-LOTE-01',@OrdersWarehouseId,DATEADD(day,-2,SYSUTCDATETIME()),
               CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}')),
              (@SecondOrderId,@BusinessId,0,0,2,N'Cliente dos',N'1002',N'COP',
               20000,0,20000,1,N'PED-LOTE-02',@OrdersWarehouseId,DATEADD(day,-1,SYSUTCDATETIME()),
               CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}'));

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES
              (NEWID(),@FirstOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',1,10000,0,10000,DATEADD(day,-2,SYSUTCDATETIME())),
              (NEWID(),@SecondOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',2,10000,0,20000,DATEADD(day,-1,SYSUTCDATETIME()));
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        command.Parameters.AddWithValue("@OrdersWarehouseCode", $"PED-{ordersWarehouseId:N}"[..32]);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Username", $"batch-{userId:N}");
        command.Parameters.AddWithValue("@FirstOrderId", firstOrderId);
        command.Parameters.AddWithValue("@SecondOrderId", secondOrderId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        await command.ExecuteNonQueryAsync();
    }
}
