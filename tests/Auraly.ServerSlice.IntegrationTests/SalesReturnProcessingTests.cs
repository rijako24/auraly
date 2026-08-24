using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class SalesReturnProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Return_flows_once_through_api_motor_inventory_and_refund()
    {
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_501));
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", await JobStatusAsync(original.DocumentId));
        var afterSale = await QuantityAsync();
        var valueAfterSale = await InventoryValueAsync();
        var recognizedCost = await OriginalRecognizedCostAsync(original.DocumentId);

        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, "Cash", "Cliente devuelve parcialmente",
            [new ConfirmSalesReturnLineRequest(
                1, .5m, ReturnInventoryDispositions.Sellable)],
            fixture.WorkSessionId, 1, "Other");
        const string idempotencyKey = "sales-return-e2e-001";
        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Read, SalesReturnPermissionCodes.Create,
            SalesReturnPermissionCodes.Confirm);
        using (var message = Message(request, idempotencyKey))
        using (var response = await user.SendAsync(message))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());
            var accepted = await response.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
            Assert.NotNull(accepted);
            Assert.StartsWith("DVT00-", accepted.DocumentNumber);
            Assert.False(accepted.IdempotentReplay);
        }

        Assert.Equal("Completed", await JobStatusAsync(request.ReturnId));
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.SalesReturns WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(afterSale + .5m, await QuantityAsync());
        Assert.Equal(decimal.Round(valueAfterSale + (.5m * recognizedCost), 4, MidpointRounding.AwayFromZero), await InventoryValueAsync());
        Assert.Equal(5_000m, await ScalarAsync<decimal>(
            "SELECT SUM(UntaxedAmount) FROM dbo.SalesReturnLines WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(950m, await ScalarAsync<decimal>(
            "SELECT SUM(TaxAmount) FROM dbo.SalesReturnLines WHERE ReturnId=@Id", request.ReturnId));

        Assert.Equal(5_950m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.SalesReturnSettlements WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(1, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));
        Assert.Equal(1, await CountAsync("SalesReturnSettlements", "ReturnId", request.ReturnId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", "DocumentId", request.ReturnId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));
        Assert.Equal(-5_950m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));

        using (var listResponse = await user.GetAsync(
                   "/api/commerce/v1/sales-returns?page=1&pageSize=20&search=DVT00"))
        {
            listResponse.EnsureSuccessStatusCode();
            var page = await listResponse.Content.ReadFromJsonAsync<SalesReturnPage>();
            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.ReturnId == request.ReturnId);
        }
        using (var detailResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/{request.ReturnId:D}"))
        {
            detailResponse.EnsureSuccessStatusCode();
            var detail = await detailResponse.Content.ReadFromJsonAsync<SalesReturnDetail>();
            Assert.NotNull(detail);
            Assert.Equal("Other", detail.ReasonCode);
            Assert.Single(detail.Lines);
        }

        using (var salesResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales?page=1&pageSize=20&search={Uri.EscapeDataString(original.DocumentNumber.FullNumber)}&withAvailableQuantity=true"))
        {
            Assert.True(salesResponse.IsSuccessStatusCode,
                await salesResponse.Content.ReadAsStringAsync());
            var page = await salesResponse.Content.ReadFromJsonAsync<ReturnableSalePage>();
            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.DocumentId == original.DocumentId);
        }
        using (var saleResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales/{original.DocumentId:D}"))
        {
            saleResponse.EnsureSuccessStatusCode();
            var sale = await saleResponse.Content.ReadFromJsonAsync<ReturnableSale>();
            Assert.NotNull(sale);
            Assert.Equal(.5m, Assert.Single(sale.Lines).AvailableQuantity);
            Assert.Equal(5_950m, Assert.Single(sale.Payments).AvailableAmount);
        }


        using (var replayMessage = Message(request, idempotencyKey))
        using (var replayResponse = await user.SendAsync(replayMessage))
        {
            Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
            var replay = await replayResponse.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
        }
        Assert.Equal(afterSale + .5m, await QuantityAsync());
        Assert.Equal(1, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));

        var firstConcurrent = request with { ReturnId = Guid.NewGuid() };
        var secondConcurrent = request with { ReturnId = Guid.NewGuid() };
        using var firstConcurrentMessage = Message(
            firstConcurrent, $"concurrent-{firstConcurrent.ReturnId:N}");
        using var secondConcurrentMessage = Message(
            secondConcurrent, $"concurrent-{secondConcurrent.ReturnId:N}");
        var concurrentResponses = await Task.WhenAll(
            user.SendAsync(firstConcurrentMessage),
            user.SendAsync(secondConcurrentMessage));
        try
        {
            Assert.Single(concurrentResponses.Where(
                response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Single(concurrentResponses.Where(
                response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in concurrentResponses) response.Dispose();
        }
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesReturnLines WHERE OriginalDocumentId=@Id",
            original.DocumentId));


        var excessive = request with
        {
            ReturnId = Guid.NewGuid(),
            Lines = [new ConfirmSalesReturnLineRequest(
                1, .6m, ReturnInventoryDispositions.Sellable)]
        };
        using var excessiveMessage = Message(excessive, $"excess-{Guid.NewGuid():N}");
        using var excessiveResponse = await user.SendAsync(excessiveMessage);
        Assert.Equal(HttpStatusCode.Conflict, excessiveResponse.StatusCode);
    }

    [Fact]
    public async Task Return_requires_backend_permissions_and_authenticated_business()
    {
        using var denied = fixture.CreateAdminClient(SalesReturnPermissionCodes.Create);
        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, Guid.NewGuid(),
            DateTimeOffset.UtcNow, ReturnEconomicResolutions.Refund, "Cash", "Prueba",
            [new ConfirmSalesReturnLineRequest(1, 1m, ReturnInventoryDispositions.Sellable)],
            ReasonCode: "Other");
        using (var deniedMessage = Message(request, $"denied-{Guid.NewGuid():N}"))
        using (var deniedResponse = await denied.SendAsync(deniedMessage))
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        using (var deniedRead = await denied.GetAsync(
                   "/api/commerce/v1/sales-returns?page=1&pageSize=20"))
            Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm);
        using var wrongScope = Message(
            request with { BusinessId = Guid.NewGuid() }, $"scope-{Guid.NewGuid():N}");
        using var wrongScopeResponse = await allowed.SendAsync(wrongScope);
        Assert.Equal(HttpStatusCode.Forbidden, wrongScopeResponse.StatusCode);
    }

    private static HttpRequestMessage Message(
        ConfirmSalesReturnRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract(
            "11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(
            ServerSliceFixture.SupplierTaxId, "7", "31", "1",
            "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract(
            "222222222", "0", "13", "2", "CLIENTE HISTORICO", "CLIENTE HISTORICO",
            "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(
                fixture.FiscalIssuerConfigurationId, "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract(
                    ServerSliceFixture.AuthorizationNumber,
                    new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
                    ServerSliceFixture.Prefix, 1, 10000),
                "auraly-test-software",
                [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot!.IssuedAt.Date), null)
        };
    }

    private async Task<decimal> QuantityAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT QuantityOnHand FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<string> JobStatusAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private async Task<decimal> InventoryValueAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT InventoryValue FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<decimal> OriginalRecognizedCostAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RecognizedUnitCost FROM dbo.InventoryMovements
            WHERE DocumentId=@Id AND DocumentType=N'SalesInvoice' AND LineNumber=1;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", id);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected SQL value was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private async Task<int> CountAsync(string table, string column, Guid id)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "InventoryMovements:DocumentId",
            "SalesReturnSettlements:ReturnId",
            "ServerOutboxMessages:DocumentId"
        };
        Assert.Contains($"{table}:{column}", allowed);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id";
        command.Parameters.AddWithValue("@Id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
