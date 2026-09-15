using System.Net.Http.Json;
using System.Diagnostics;
using Auraly.Application.Orders;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OrderCreditInvoiceTests(
    ServerSliceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Credit_batch_rejects_aggregated_customer_before_creating_any_effect()
    {
        var scenario = await SeedAsync(creditLimit: 25_000m);
        using var client = CreateClient(scenario.UserId);

        var key = $"orders-credit-rejected-{Guid.NewGuid():N}";
        var response = await InvoiceAsync(client, scenario, "Credit", key);
        var replay = await InvoiceAsync(client, scenario, "Credit", key);

        Assert.Equal("CreditRejected", response.Status);
        Assert.Equal(Guid.Empty, response.OperationId);
        Assert.Equal(2, response.RequestedCount);
        Assert.Equal(0, response.CompletedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Empty(response.Results);
        Assert.True(replay.IsReplay);
        Assert.Equal(response.CreditValidationIssues, replay.CreditValidationIssues);
        var issue = Assert.Single(response.CreditValidationIssues!);
        Assert.Equal(scenario.CustomerId, issue.CustomerId);
        Assert.Equal(30_000m, issue.RequestedAmount);
        Assert.Equal(25_000m, issue.AvailableCredit);
        Assert.Contains("cupo", issue.Reason, StringComparison.OrdinalIgnoreCase);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId)),
              (SELECT COUNT(*) FROM dbo.OrderInvoiceBatchReceipts
               WHERE UserId=@UserId AND BusinessId=@BusinessId
                 AND Status=N'CreditRejected'),
              (SELECT COUNT(*) FROM dbo.Orders
               WHERE OrderId IN (@FirstOrderId,@SecondOrderId)
                 AND ReleaseTransferId IS NOT NULL);
            """;
        verify.Parameters.AddWithValue("@FirstOrderId", scenario.FirstOrderId);
        verify.Parameters.AddWithValue("@SecondOrderId", scenario.SecondOrderId);
        verify.Parameters.AddWithValue("@UserId", scenario.UserId);
        verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    [Fact]
    public async Task Credit_batch_rejects_customer_without_credit_enabled_before_creating_business_effects()
    {
        var scenario = await SeedAsync(creditLimit: 100_000m, creditEnabled: false);
        using var client = CreateClient(scenario.UserId);

        var response = await InvoiceAsync(client, scenario, "Credit");

        Assert.Equal("CreditRejected", response.Status);
        Assert.Equal(Guid.Empty, response.OperationId);
        Assert.Equal(0, response.CompletedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Empty(response.Results);
        var issue = Assert.Single(response.CreditValidationIssues!);
        Assert.Equal(scenario.CustomerId, issue.CustomerId);
        Assert.Null(issue.AvailableCredit);
        Assert.Contains("no tiene habilitada", issue.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Credit_batch_rejects_an_inactive_customer_before_creating_business_effects()
    {
        var scenario = await SeedAsync(creditLimit: 100_000m, customerActive: false);
        using var client = CreateClient(scenario.UserId);

        var response = await InvoiceAsync(client, scenario, "Credit");

        Assert.Equal("CreditRejected", response.Status);
        Assert.Equal(Guid.Empty, response.OperationId);
        Assert.Empty(response.Results);
        var issue = Assert.Single(response.CreditValidationIssues!);
        Assert.Equal(scenario.CustomerId, issue.CustomerId);
        Assert.Null(issue.AvailableCredit);
        Assert.Contains("no tiene habilitada", issue.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Valid_credit_batch_uses_canonical_checkout_and_creates_one_receivable_per_order()
    {
        await WarmCreditInvoicePathAsync();

        var scenario = await SeedAsync(creditLimit: 100_000m);
        using var client = CreateClient(scenario.UserId);

        fixture.PauseDocumentProcessing();
        InvoiceOrdersResponse response;
        var timing = Stopwatch.StartNew();
        try
        {
            response = await InvoiceAsync(client, scenario, "Credit");
            timing.Stop();
            Assert.True(
                string.Equals("Completed", response.Status, StringComparison.Ordinal),
                $"Credit batch ended as {response.Status}: " +
                string.Join(" | ", response.Results.Select(result =>
                    $"{result.OrderNumber}: {result.Error ?? result.Status}")));
            Assert.Equal(2, response.CompletedCount);
            Assert.Equal(0, response.FailedCount);
            Assert.True(
                timing.Elapsed < TimeSpan.FromSeconds(response.CompletedCount),
                $"El lote a crédito promedió " +
                $"{timing.Elapsed.TotalMilliseconds / response.CompletedCount:N0} ms por factura.");
            output.WriteLine(
                "Facturación masiva a crédito: {0:N0} ms por factura ({1} documentos).",
                timing.Elapsed.TotalMilliseconds / response.CompletedCount,
                response.CompletedCount);
            Assert.True(response.CreditValidationIssues is null or { Count: 0 });
            Assert.All(response.Results, result =>
            {
                Assert.Equal("Invoiced", result.Status);
                Assert.NotNull(result.DocumentId);
                Assert.NotNull(result.Receipt?.CreditAcknowledgement);
                var creditPayment = Assert.Single(result.Receipt!.Payments);
                Assert.Equal("Credit", creditPayment.MethodCode);
                Assert.Equal(result.Receipt.PayableAmount, creditPayment.Amount);
            });

            var signals = fixture.DrainDocumentSignals();
            Assert.Equal(2, signals.Count);
            fixture.ResumeDocumentProcessing();
            foreach (var signal in signals)
                await fixture.DocumentSignals.PublishAsync(signal);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.Receivables
               WHERE CustomerId=@CustomerId AND SourceDocumentId IN (
                 SELECT DocumentId FROM dbo.OrderInvoiceLinks
                 WHERE OrderId IN (@FirstOrderId,@SecondOrderId))),
              (SELECT COALESCE(SUM(OutstandingAmount),0) FROM dbo.Receivables
               WHERE CustomerId=@CustomerId AND SourceDocumentId IN (
                 SELECT DocumentId FROM dbo.OrderInvoiceLinks
                 WHERE OrderId IN (@FirstOrderId,@SecondOrderId))),
              (SELECT COALESCE(SUM(CreditAmount),0) FROM dbo.SalesDocuments
               WHERE DocumentId IN (
                 SELECT DocumentId FROM dbo.OrderInvoiceLinks
                 WHERE OrderId IN (@FirstOrderId,@SecondOrderId)));
            """;
        verify.Parameters.AddWithValue("@CustomerId", scenario.CustomerId);
        verify.Parameters.AddWithValue("@FirstOrderId", scenario.FirstOrderId);
        verify.Parameters.AddWithValue("@SecondOrderId", scenario.SecondOrderId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.True(reader.GetDecimal(1) > 0);
        Assert.Equal(reader.GetDecimal(2), reader.GetDecimal(1));
    }

    private HttpClient CreateClient(Guid userId) => fixture.CreateUserClient(
        userId,
        CommercePermissionCodes.SalesCreate,
        OrderPermissionCodes.Read,
        OrderPermissionCodes.Recover,
        OrderPermissionCodes.Invoice);

    private async Task WarmCreditInvoicePathAsync()
    {
        var scenario = await SeedAsync(creditLimit: 100_000m);
        using var client = CreateClient(scenario.UserId);
        fixture.PauseDocumentProcessing();
        try
        {
            var response = await InvoiceAsync(client, scenario, "Credit");
            Assert.True(
                string.Equals("Completed", response.Status, StringComparison.Ordinal),
                $"Warm credit batch ended as {response.Status}: " +
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

    private static async Task<InvoiceOrdersResponse> InvoiceAsync(
        HttpClient client,
        Scenario scenario,
        string paymentMethodCode,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/commerce/v1/orders/invoice")
        {
            Content = JsonContent.Create(new InvoiceOrdersRequest(
                scenario.WorkSessionId,
                scenario.WarehouseId,
                scenario.UserId,
                [scenario.FirstOrderId, scenario.SecondOrderId],
                paymentMethodCode,
                null,
                DocumentType: "SalesReceipt"))
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey ?? $"orders-credit-{Guid.NewGuid():N}");
        using var result = await client.SendAsync(request);
        var body = await result.Content.ReadAsStringAsync();
        Assert.True(result.IsSuccessStatusCode, body);
        return System.Text.Json.JsonSerializer.Deserialize<InvoiceOrdersResponse>(
                   body,
                   new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Empty order credit response.");
    }

    private async Task<Scenario> SeedAsync(
        decimal creditLimit,
        bool creditEnabled = true,
        bool customerActive = true)
    {
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var ordersWarehouseId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
              N'Cajero',N'Crédito',1,SYSDATETIMEOFFSET());

            INSERT dbo.WorkSessions(
              WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
              OpenedAt,LastActivityAt,Status)
            VALUES(@WorkSessionId,@TenantId,@BusinessId,@WarehouseId,@UserId,NULL,
              SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),N'Open');

            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,
              IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Cliente crédito por lote',
              N'Cliente crédito por lote',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@BusinessId,@CustomerActive,@UserId,SYSDATETIMEOFFSET());

            DECLARE @CountryId UNIQUEIDENTIFIER,@DivisionId UNIQUEIDENTIFIER,@CityId UNIQUEIDENTIFIER;
            SELECT TOP(1) @CountryId=country.CountryId,
                          @DivisionId=division.AdministrativeDivisionId,
                          @CityId=city.CityId
            FROM dbo.Cities city
            JOIN dbo.AdministrativeDivisions division
              ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
            JOIN dbo.Countries country ON country.CountryId=division.CountryId
            WHERE city.IsActive=1 AND division.IsActive=1 AND country.IsActive=1;
            INSERT dbo.PartySites(
              PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
              CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartySiteId,@PartyId,@SiteCode,N'Sede principal',@CountryId,
              @DivisionId,@CityId,N'Calle crédito 1',1,1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.CustomerCreditProfiles(
              CustomerId,BusinessId,CreditLimit,DefaultDueDays,IsCreditEnabled,
              UpdatedByUserId,UpdatedAt)
            VALUES(@CustomerId,@BusinessId,@CreditLimit,30,@CreditEnabled,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.Warehouses(
              WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
              IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
            VALUES(@OrdersWarehouseId,@BusinessId,@OrdersWarehouseCode,N'Pedidos crédito',0,
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
              OrderId,BusinessId,Source,FulfillmentMode,Status,CustomerId,PartySiteId,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,ExternalDocumentNumber,
              OrdersWarehouseId,CreatedAt,CustomAttributesJson)
            VALUES
              (@FirstOrderId,@BusinessId,0,0,2,@CustomerId,@PartySiteId,N'Cliente crédito por lote',
               @Identification,N'COP',10000,0,10000,1,@FirstNumber,@OrdersWarehouseId,
               DATEADD(day,-2,SYSUTCDATETIME()),CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}')),
              (@SecondOrderId,@BusinessId,0,0,2,@CustomerId,@PartySiteId,N'Cliente crédito por lote',
               @Identification,N'COP',20000,0,20000,1,@SecondNumber,@OrdersWarehouseId,
               DATEADD(day,-1,SYSUTCDATETIME()),CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}'));

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES
              (NEWID(),@FirstOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto crédito',N'EA',1,10000,0,10000,DATEADD(day,-2,SYSUTCDATETIME())),
              (NEWID(),@SecondOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto crédito',N'EA',2,10000,0,20000,DATEADD(day,-1,SYSUTCDATETIME()));
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        command.Parameters.AddWithValue("@OrdersWarehouseCode", $"PED-{ordersWarehouseId:N}"[..32]);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@PartySiteId", partySiteId);
        command.Parameters.AddWithValue("@SiteCode", $"SITE-{partySiteId:N}"[..24]);
        command.Parameters.AddWithValue("@CreditLimit", creditLimit);
        command.Parameters.AddWithValue("@CreditEnabled", creditEnabled);
        command.Parameters.AddWithValue("@CustomerActive", customerActive);
        command.Parameters.AddWithValue("@Identification", $"9{Random.Shared.NextInt64(100000000, 999999999)}");
        command.Parameters.AddWithValue("@Username", $"order-credit-{userId:N}");
        command.Parameters.AddWithValue("@Email", $"order-credit-{userId:N}@test.local");
        command.Parameters.AddWithValue("@FirstOrderId", firstOrderId);
        command.Parameters.AddWithValue("@SecondOrderId", secondOrderId);
        command.Parameters.AddWithValue("@FirstNumber", $"PED-CRED-{firstOrderId:N}"[..24]);
        command.Parameters.AddWithValue("@SecondNumber", $"PED-CRED-{secondOrderId:N}"[..24]);
        await command.ExecuteNonQueryAsync();
        return new(userId, workSessionId, fixture.WarehouseId, customerId, firstOrderId, secondOrderId);
    }

    private sealed record Scenario(
        Guid UserId,
        Guid WorkSessionId,
        Guid WarehouseId,
        Guid CustomerId,
        Guid FirstOrderId,
        Guid SecondOrderId);
}
