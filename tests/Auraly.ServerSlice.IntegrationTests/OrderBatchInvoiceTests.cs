using System.Diagnostics;
using System.Net.Http.Json;
using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OrderBatchInvoiceTests(
    ServerSliceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Fractional_order_uses_its_closed_line_total_without_a_rounding_discount()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var arrange = connection.CreateCommand();
            arrange.CommandText = """
                UPDATE dbo.Orders
                SET Subtotal=4602.13,DiscountTotal=0,TaxTotal=0,Total=4602.13
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId;

                UPDATE dbo.OrderItems
                SET Quantity=.3,UnitPrice=15340.45,DiscountAmount=0,
                    TaxAmount=0,LineTotal=4602.13,
                    RawPayloadJson=JSON_MODIFY(
                        JSON_MODIFY(RawPayloadJson,'$.TaxCode','01'),
                        '$.TaxRate',0)
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
                """;
            arrange.Parameters.AddWithValue("@OrderId", orderId);
            arrange.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            Assert.Equal(2, await arrange.ExecuteNonQueryAsync());
        }

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        fixture.PauseDocumentProcessing();
        Guid documentId;
        try
        {
            var response = await InvoiceAsync(
                client,
                new InvoiceOrdersRequest(
                    workSessionId, fixture.WarehouseId, userId, [orderId], "Cash", null),
                $"closed-line-total-{Guid.NewGuid():N}");
            Assert.True(response.Status == "Completed",
                string.Join(" | ", response.Results.Select(result => result.Error)));
            var result = Assert.Single(response.Results);
            Assert.Equal("Invoiced", result.Status);
            documentId = result.DocumentId
                ?? throw new InvalidOperationException("The order did not produce a document.");

            var signal = Assert.Single(fixture.DrainDocumentSignals());
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT line.UnitPrice,line.DiscountAmount,line.UntaxedAmount,line.LineTotal,
                   document.FiscalStatus,receipt.Status
            FROM dbo.SalesDocumentLines line
            JOIN dbo.SalesDocuments document ON document.DocumentId=line.DocumentId
            JOIN dbo.OnlineSalesCheckoutReceipts receipt ON receipt.DocumentId=line.DocumentId
            WHERE line.DocumentId=@DocumentId;
            """;
        verify.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(15_340.4333m, reader.GetDecimal(0));
        Assert.Equal(0m, reader.GetDecimal(1));
        Assert.Equal(4_602.13m, reader.GetDecimal(2));
        Assert.Equal(4_602.13m, reader.GetDecimal(3));
        Assert.NotEqual("FiscalIntegrityConflict", reader.GetString(4));
        Assert.Equal("Completed", reader.GetString(5));
    }

    [Fact]
    public async Task Generic_order_line_is_invoiced_from_its_snapshot_without_inventory_reinterpretation()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var freeze = connection.CreateCommand();
            freeze.CommandText = """
                UPDATE dbo.OrderItems
                SET ProductCodeSnapshot=N'GEN-CONGELADO',
                    ProductNameSnapshot=N'Servicio congelado del pedido',
                    DescriptionSnapshot=N'Servicio congelado del pedido',
                    IsGenericProductSnapshot=1
                WHERE OrderId=@OrderId;
                """;
            freeze.Parameters.AddWithValue("@OrderId", orderId);
            Assert.Equal(1, await freeze.ExecuteNonQueryAsync());
        }

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        fixture.PauseDocumentProcessing();
        Guid documentId;
        try
        {
            var response = await InvoiceAsync(
                client,
                new InvoiceOrdersRequest(
                    workSessionId, fixture.WarehouseId, userId, [orderId], "Cash", null),
                $"generic-order-{Guid.NewGuid():N}");
            documentId = Assert.Single(response.Results).DocumentId
                ?? throw new InvalidOperationException("The generic order did not produce a document.");
            var signal = Assert.Single(fixture.DrainDocumentSignals());
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT line.IsGenericProductSnapshot,line.ProductCodeSnapshot,line.ProductNameSnapshot,
                   (SELECT COUNT(*) FROM dbo.InventoryMovements movement
                    WHERE movement.DocumentId=line.DocumentId)
            FROM dbo.SalesDocumentLines line
            WHERE line.DocumentId=@DocumentId;
            """;
        verify.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal("GEN-CONGELADO", reader.GetString(1));
        Assert.Equal("Servicio congelado del pedido", reader.GetString(2));
        Assert.Equal(0, reader.GetInt32(3));
    }

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
        await WarmCashInvoicePathAsync();

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
        var timing = Stopwatch.StartNew();
        try
        {
            first = await InvoiceAsync(client, command, idempotencyKey);
            timing.Stop();
            var queued = fixture.DrainDocumentSignals();
            Assert.True(
                queued.Count == 2,
                $"Expected two processing signals but found {queued.Count}. " +
                string.Join(" | ", first.Results.Select(result =>
                    $"{result.OrderNumber}:{result.Status}:{result.Error ?? "none"}")));
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
        Assert.True(
            timing.Elapsed < TimeSpan.FromSeconds(first.CompletedCount),
            $"El lote en efectivo promedió " +
            $"{timing.Elapsed.TotalMilliseconds / first.CompletedCount:N0} ms por factura.");
        output.WriteLine(
            "Facturación masiva en efectivo: {0:N0} ms por factura ({1} documentos).",
            timing.Elapsed.TotalMilliseconds / first.CompletedCount,
            first.CompletedCount);
        Assert.False(first.IsReplay);
        Assert.Equal(2, first.Results.Count);
        Assert.All(first.Results, result =>
        {
            Assert.Equal("Invoiced", result.Status);
            Assert.NotNull(result.DocumentId);
            Assert.NotNull(result.DocumentNumber);
            Assert.NotNull(result.Receipt);
            Assert.Equal(result.DocumentId, result.Receipt!.DocumentId);
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
        Assert.All(replay.Results, result => Assert.NotNull(result.Receipt));

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

    private async Task WarmCashInvoicePathAsync()
    {
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
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

        fixture.PauseDocumentProcessing();
        try
        {
            var response = await InvoiceAsync(client, command, $"warm-cash-{Guid.NewGuid():N}");
            Assert.True(
                string.Equals("Completed", response.Status, StringComparison.Ordinal),
                $"Warm cash batch ended as {response.Status}: " +
                string.Join(" | ", response.Results.Select(result =>
                    $"{result.OrderNumber}: {result.Error ?? result.Status}")));
            Assert.Equal(2, response.CompletedCount);
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
    }

    [Fact]
    public async Task Sales_receipt_is_returned_with_order_emission_without_a_fiscal_lookup()
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
            null,
            DocumentType: "SalesReceipt");

        fixture.PauseDocumentProcessing();
        try
        {
            var response = await InvoiceAsync(
                client,
                command,
                $"sales-receipt-{Guid.NewGuid():N}");
            var result = Assert.Single(response.Results);
            Assert.True(
                result.Receipt is not null,
                $"Order emission ended as {result.Status}: {result.Error ?? "no detail"}.");
            var receipt = Assert.IsType<Auraly.Contracts.Sales.OnlineSalesReceipt>(
                result.Receipt);

            Assert.Equal("Invoiced", result.Status);
            Assert.Equal("SalesReceipt", receipt.DocumentType);
            Assert.Equal(result.DocumentId, receipt.DocumentId);
            Assert.Null(receipt.Cufe);
            Assert.Null(receipt.QrPayload);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            foreach (var signal in fixture.DrainDocumentSignals())
                await fixture.DocumentSignals.PublishAsync(signal);
        }
    }

    [Fact]
    public async Task Sales_receipt_consumes_exact_PED_reservation_without_releasing_stock()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var exactReservation = connection.CreateCommand();
            exactReservation.CommandText = """
                UPDATE balance
                SET QuantityOnHand=1,InventoryValue=AverageUnitCost,
                    UpdatedAt=SYSDATETIMEOFFSET()
                FROM dbo.InventoryBalances balance
                JOIN dbo.Orders orders
                  ON orders.BusinessId=balance.BusinessId
                 AND orders.OrdersWarehouseId=balance.WarehouseId
                WHERE orders.OrderId=@OrderId
                  AND balance.ProductId=@ProductId;

                UPDATE dbo.InventoryBalances
                SET QuantityOnHand=0,InventoryValue=0,
                    UpdatedAt=SYSDATETIMEOFFSET()
                WHERE BusinessId=@BusinessId
                  AND WarehouseId=@WarehouseId
                  AND ProductId=@ProductId;
                """;
            exactReservation.Parameters.AddWithValue("@OrderId", orderId);
            exactReservation.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            exactReservation.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            exactReservation.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            Assert.InRange(await exactReservation.ExecuteNonQueryAsync(), 1, 2);
        }

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
            null,
            DocumentType: "SalesReceipt");

        fixture.PauseDocumentProcessing();
        try
        {
            var response = await InvoiceAsync(
                client,
                command,
                $"exact-reservation-{Guid.NewGuid():N}");
            var result = Assert.Single(response.Results);

            Assert.Equal("Completed", response.Status);
            Assert.Equal(1, response.CompletedCount);
            Assert.Equal(0, response.FailedCount);
            Assert.Equal("Invoiced", result.Status);
            Assert.NotNull(result.DocumentId);
            Assert.Null(result.Error);

            var signal = Assert.Single(fixture.DrainDocumentSignals());
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);

            await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var verify = verifyConnection.CreateCommand();
            verify.CommandText = """
                SELECT
                  orders.ExternalStatus,
                  orders.ReleaseTransferId,
                  COALESCE(ordersBalance.QuantityOnHand,0),
                  COALESCE(salesBalance.QuantityOnHand,0),
                  (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks link
                   WHERE link.OrderId=@OrderId),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements movement
                   WHERE movement.DocumentId=@DocumentId
                     AND movement.WarehouseId=orders.OrdersWarehouseId
                     AND movement.MovementType=N'Sale'),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements movement
                   WHERE movement.DocumentType=N'WarehouseTransfer'
                     AND movement.DocumentId=orders.ReleaseTransferId)
                FROM dbo.Orders orders
                LEFT JOIN dbo.InventoryBalances ordersBalance
                  ON ordersBalance.BusinessId=orders.BusinessId
                 AND ordersBalance.WarehouseId=orders.OrdersWarehouseId
                 AND ordersBalance.ProductId=@ProductId
                LEFT JOIN dbo.InventoryBalances salesBalance
                  ON salesBalance.BusinessId=orders.BusinessId
                 AND salesBalance.WarehouseId=@WarehouseId
                 AND salesBalance.ProductId=@ProductId
                WHERE orders.OrderId=@OrderId;
                """;
            verify.Parameters.AddWithValue("@OrderId", orderId);
            verify.Parameters.AddWithValue("@DocumentId", result.DocumentId!.Value);
            verify.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            verify.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("InventoryConsumedByInvoice", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(0m, reader.GetDecimal(2));
            Assert.Equal(0m, reader.GetDecimal(3));
            Assert.Equal(1, reader.GetInt32(4));
            Assert.Equal(1, reader.GetInt32(5));
            Assert.Equal(0, reader.GetInt32(6));
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            foreach (var signal in fixture.DrainDocumentSignals())
                await fixture.DocumentSignals.PublishAsync(signal);
            await using var restoreConnection = new SqlConnection(fixture.ConnectionString);
            await restoreConnection.OpenAsync();
            await using var restore = restoreConnection.CreateCommand();
            restore.CommandText = """
                UPDATE dbo.InventoryBalances
                SET QuantityOnHand=100,AverageUnitCost=5000,InventoryValue=500000,
                    UpdatedAt=SYSDATETIMEOFFSET()
                WHERE BusinessId=@BusinessId
                  AND WarehouseId=@WarehouseId
                  AND ProductId=@ProductId;
                """;
            restore.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            restore.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            restore.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Credit_order_with_customer_site_is_invoiced_without_server_error()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seedCredit = connection.CreateCommand();
            seedCredit.CommandText = """
                DECLARE @CountryId UNIQUEIDENTIFIER,
                        @DivisionId UNIQUEIDENTIFIER,
                        @CityId UNIQUEIDENTIFIER;
                SELECT TOP(1)
                  @CountryId=country.CountryId,
                  @DivisionId=division.AdministrativeDivisionId,
                  @CityId=city.CityId
                FROM dbo.Cities city
                JOIN dbo.AdministrativeDivisions division
                  ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
                JOIN dbo.Countries country ON country.CountryId=division.CountryId
                WHERE city.IsActive=1 AND division.IsActive=1 AND country.IsActive=1;

                INSERT dbo.Parties(
                  PartyId,TenantId,PartyType,IdentificationCountryId,
                  IdentificationTypeCode,Identification,NormalizedIdentification,
                  DisplayName,LegalName,
                  CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(
                  @PartyId,@TenantId,N'Organization',@CountryId,
                  N'31',N'900123456',N'900123456',
                  N'Cliente crédito lote',
                  N'Cliente crédito lote',N'Complete',1,@UserId,SYSDATETIMEOFFSET());

                INSERT dbo.Customers(
                  CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
                  IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@BusinessId,1,1,@UserId,SYSDATETIMEOFFSET());

                INSERT dbo.PartySites(
                  PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
                  CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                VALUES(
                  @PartySiteId,@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,
                  @DivisionId,@CityId,N'Calle 1',1,1,@UserId,SYSDATETIMEOFFSET());

                INSERT dbo.CustomerCreditProfiles(
                  CustomerId,BusinessId,CreditLimit,DefaultDueDays,IsCreditEnabled,
                  UpdatedByUserId,UpdatedAt)
                VALUES(@CustomerId,@BusinessId,1000000,30,1,@UserId,SYSDATETIMEOFFSET());

                -- Simula un pedido existente cuyo encabezado quedó un centavo
                -- por debajo de la suma canónica de sus líneas.
                UPDATE dbo.Orders
                SET CustomerId=@CustomerId,PartySiteId=@PartySiteId,
                    CustomerNameSnapshot=N'Cliente crédito lote',
                    Subtotal=5908.89,Total=5908.89
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId;

                UPDATE dbo.OrderItems
                SET Quantity=.5,UnitPrice=11817.83,DiscountAmount=.01,LineTotal=5908.90
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
                """;
            seedCredit.Parameters.AddWithValue("@PartyId", partyId);
            seedCredit.Parameters.AddWithValue("@CustomerId", customerId);
            seedCredit.Parameters.AddWithValue("@PartySiteId", partySiteId);
            seedCredit.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seedCredit.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seedCredit.Parameters.AddWithValue("@UserId", userId);
            seedCredit.Parameters.AddWithValue("@OrderId", orderId);
            await seedCredit.ExecuteNonQueryAsync();
        }

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
            "Credit",
            null);

        fixture.PauseDocumentProcessing();
        try
        {
            var response = await InvoiceAsync(
                client,
                command,
                $"credit-order-{Guid.NewGuid():N}");
            Assert.Equal("Completed", response.Status);
            Assert.Equal(1, response.CompletedCount);
            Assert.Equal(0, response.FailedCount);
            Assert.Null(response.CreditValidationIssues);
            var result = Assert.Single(response.Results);
            Assert.Equal("Invoiced", result.Status);
            Assert.NotNull(result.DocumentId);

            var signal = Assert.Single(fixture.DrainDocumentSignals());
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);

            await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var verify = verifyConnection.CreateCommand();
            verify.CommandText = """
                SELECT document.CreditAmount,document.PayableAmount,document.CustomerPartySiteId,
                       receivable.PartySiteId,receivable.OutstandingAmount
                FROM dbo.SalesDocuments document
                JOIN dbo.Receivables receivable
                  ON receivable.SourceDocumentId=document.DocumentId
                WHERE document.DocumentId=@DocumentId;
                """;
            verify.Parameters.AddWithValue("@DocumentId", result.DocumentId.Value);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(5900m, reader.GetDecimal(0));
            Assert.Equal(5900m, reader.GetDecimal(1));
            Assert.Equal(partySiteId, reader.GetGuid(2));
            Assert.Equal(partySiteId, reader.GetGuid(3));
            Assert.Equal(5900m, reader.GetDecimal(4));
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            foreach (var signal in fixture.DrainDocumentSignals())
                await fixture.DocumentSignals.PublishAsync(signal);
        }
    }

    [Fact]
    public async Task Invalid_order_in_a_batch_is_terminal_and_replays_without_a_stale_lease()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dbo.Orders
                SET CustomAttributesJson=CONCAT(
                    N'{"WarehouseId":"',CONVERT(nvarchar(36),NEWID()),N'"}')
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
                """;
            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Invoice);
        var commandRequest = new InvoiceOrdersRequest(
            workSessionId, fixture.WarehouseId, userId,
            [orderId], "Cash", null);
        var idempotencyKey = $"invalid-order-{Guid.NewGuid():N}";

        var first = await InvoiceAsync(client, commandRequest, idempotencyKey);
        var replay = await InvoiceAsync(client, commandRequest, idempotencyKey);

        Assert.Equal("Failed", first.Status);
        Assert.Equal(0, first.CompletedCount);
        Assert.Equal(1, first.FailedCount);
        Assert.False(first.IsReplay);
        var failedResult = Assert.Single(first.Results);
        Assert.Equal("Failed", failedResult.Status);
        Assert.Equal("PED-LOTE-01", failedResult.OrderNumber);
        Assert.False(string.IsNullOrWhiteSpace(failedResult.Error));
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OperationId, replay.OperationId);

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT Status,CompletedAt
            FROM dbo.OrderInvoiceBatchReceipts
            WHERE OperationId=@OperationId AND BusinessId=@BusinessId;
            """;
        verify.Parameters.AddWithValue("@OperationId", first.OperationId);
        verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Failed", reader.GetString(0));
        Assert.False(reader.IsDBNull(1));
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

    [Fact]
    public async Task Fiscal_conflict_releases_order_and_discards_issuing_draft()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var nextDraftId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await SeedAsync(userId, workSessionId, orderId, otherOrderId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT dbo.SalesDrafts(
                  SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,Status,Version,
                  SourceOrderId,CreatedAt,UpdatedAt)
                VALUES
                  (@DraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,N'Issuing',3,
                   @OrderId,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET()),
                  (@NextDraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,N'Active',1,
                   NULL,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());

                INSERT dbo.OrderClaims(
                  OrderClaimId,BusinessId,WarehouseId,OrderId,WorkSessionId,UserId,
                  ClaimedAt,ExpiresAt)
                VALUES(NEWID(),@BusinessId,@WarehouseId,@OrderId,@WorkSessionId,@UserId,
                       SYSDATETIMEOFFSET(),DATEADD(minute,10,SYSDATETIMEOFFSET()));

                INSERT dbo.OnlineSalesCheckoutReceipts(
                  OnlineSalesCheckoutReceiptId,BusinessId,SalesDraftId,NextSalesDraftId,
                  IdempotencyKey,RequestHash,DocumentId,PayloadJson,Status,CreatedAt)
                VALUES(NEWID(),@BusinessId,@DraftId,@NextDraftId,
                       @Key,REPLICATE('0',64),@DocumentId,N'{}',N'Prepared',SYSDATETIMEOFFSET());
                """;
            seed.Parameters.AddWithValue("@DraftId", draftId);
            seed.Parameters.AddWithValue("@NextDraftId", nextDraftId);
            seed.Parameters.AddWithValue("@DocumentId", documentId);
            seed.Parameters.AddWithValue("@OrderId", orderId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            seed.Parameters.AddWithValue("@UserId", userId);
            seed.Parameters.AddWithValue("@Key", $"fiscal-conflict-{Guid.NewGuid():N}");
            await seed.ExecuteNonQueryAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOnlineSalesCheckoutStore>();
            await store.MarkResultAsync(
                new OnlineSalesUserIdentity(userId, fixture.TenantId, new HashSet<string>()),
                draftId,
                documentId,
                "FiscalConflict",
                CancellationToken.None);
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT draft.Status,draft.SourceOrderId,draft.DeletedAt,receipt.Status,claim.ReleasedAt,
                   nextDraft.Status
            FROM dbo.SalesDrafts draft
            JOIN dbo.OnlineSalesCheckoutReceipts receipt ON receipt.SalesDraftId=draft.SalesDraftId
            JOIN dbo.OrderClaims claim ON claim.OrderId=@OrderId
            JOIN dbo.SalesDrafts nextDraft ON nextDraft.SalesDraftId=receipt.NextSalesDraftId
            WHERE draft.SalesDraftId=@DraftId;
            """;
        verify.Parameters.AddWithValue("@OrderId", orderId);
        verify.Parameters.AddWithValue("@DraftId", draftId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Deleted", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.False(reader.IsDBNull(2));
        Assert.Equal("FiscalConflict", reader.GetString(3));
        Assert.False(reader.IsDBNull(4));
        Assert.Equal("Active", reader.GetString(5));
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
              WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
              OpenedAt,LastActivityAt,Status)
            VALUES(
              @WorkSessionId,@TenantId,@BusinessId,@WarehouseId,@UserId,NULL,
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
              DocumentUnitCost,DiscountAmount,LineTotal,CreatedAt)
            VALUES
              (NEWID(),@FirstOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',1,10000,6000,0,10000,DATEADD(day,-2,SYSUTCDATETIME())),
              (NEWID(),@SecondOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',2,10000,6000,0,20000,DATEADD(day,-1,SYSUTCDATETIME()));
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
