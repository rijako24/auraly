using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class GoodsReceiptProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Receipt_flows_once_through_inventory_cost_payable_and_price_review()
    {
        var request = CreateRequest();
        var quantityBefore = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
        var valueBefore = await ReadNullableDecimalAsync("InventoryValue") ?? 0m;
        const string idempotencyKey = "receipt-e2e-001";
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);

        using (var message = CreateMessage(request, idempotencyKey))
        using (var response = await client.SendAsync(message))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
            Assert.NotNull(acceptance);
            Assert.StartsWith("EMC00-", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@ProductId AND IsActive=1",
            request.DocumentId));

        var job = await ReadJobAsync(request.DocumentId);
        Assert.True(job.Status == "Completed", job.LastError ?? job.Status);

        var quantityAfter = quantityBefore + 10m;
        var valueAfter = valueBefore + 50_000m;
        var averageAfter = decimal.Round(valueAfter / quantityAfter, 6, MidpointRounding.AwayFromZero);
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(quantityAfter, await ReadNullableDecimalAsync("QuantityOnHand"));
        Assert.Equal(valueAfter, await ReadNullableDecimalAsync("InventoryValue"));
        Assert.Equal(averageAfter, await ReadNullableDecimalAsync("AverageUnitCost"));
        Assert.Equal(5_000m, await ScalarAsync<decimal>(
            "SELECT LatestUnitCost FROM dbo.SupplierProductLatestCosts WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId",
            request.DocumentId));
        Assert.Equal(59_500m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal("PendingReview", await ScalarAsync<string>(
            "SELECT Status FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id",
            request.DocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@ProductId AND IsActive=1",
            request.DocumentId));

        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await CountAsync("SupplierCostObservations", request.DocumentId));
        Assert.Equal(1, await CountAsync("Payables", request.DocumentId));
        Assert.Equal(1, await CountAsync("PayableTransactions", request.DocumentId));
        Assert.Equal(1, await CountAsync("PriceRevisionProposals", request.DocumentId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", request.DocumentId));

        using (var duplicate = CreateMessage(request, idempotencyKey))
        using (var response = await client.SendAsync(duplicate))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var replay = await response.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(request.DocumentId, replay.DocumentId);
        }
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await CountAsync("Payables", request.DocumentId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", request.DocumentId));
    }

    [Fact]
    public async Task Zero_value_credit_receipt_processes_inventory_without_opening_an_empty_payable()
    {
        var request = CreateRequest();
        request = request with
        {
            DocumentId = Guid.NewGuid(),
            Lines = [request.Lines.Single() with { UnitCost = 0m, DiscountAmount = 0m }]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"zero-value-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var job = await ReadJobAsync(request.DocumentId);
        Assert.Equal("Completed", job.Status);
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(0, await CountAsync("Payables", request.DocumentId));
    }

    [Fact]
    public async Task Positive_cost_after_a_zero_cost_receipt_does_not_block_the_business_sequence()
    {
        var zeroCost = CreateRequest() with
        {
            DocumentId = Guid.NewGuid(),
            Lines = [CreateRequest().Lines.Single() with { UnitCost = 0m, DiscountAmount = 0m }]
        };
        var positiveCost = CreateRequest() with { DocumentId = Guid.NewGuid() };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);

        using (var message = CreateMessage(zeroCost, $"zero-before-positive-{zeroCost.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using (var message = CreateMessage(positiveCost, $"positive-after-zero-{positiveCost.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Equal("Completed", (await ReadJobAsync(zeroCost.DocumentId)).Status);
        Assert.Equal("Completed", (await ReadJobAsync(positiveCost.DocumentId)).Status);
        Assert.Equal(1, await CountAsync("InventoryMovements", positiveCost.DocumentId));
        Assert.Equal(1, await CountAsync("PriceRevisionProposals", positiveCost.DocumentId));
    }

    [Fact]
    public async Task Receipt_requires_both_backend_permissions_and_authenticated_business()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts);
        using (var denied = CreateMessage(CreateRequest(), $"denied-{Guid.NewGuid():N}"))
        using (var response = await client.SendAsync(denied))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var wrongBusiness = CreateRequest() with { BusinessId = Guid.NewGuid() };
        using var message = CreateMessage(wrongBusiness, $"scope-{Guid.NewGuid():N}");
        using var scopedResponse = await allowed.SendAsync(message);
        Assert.Equal(HttpStatusCode.Forbidden, scopedResponse.StatusCode);


        var invalidTax = CreateRequest();
        invalidTax = invalidTax with
        {
            Lines =
            [
                invalidTax.Lines.Single() with { TaxTreatment = "InferredByServer" }
            ]
        };
        using var invalidMessage = CreateMessage(
            invalidTax, $"invalid-tax-{Guid.NewGuid():N}");
        using var invalidResponse = await allowed.SendAsync(invalidMessage);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    private ConfirmGoodsReceiptRequest CreateRequest()
    {
        var received = new DateTimeOffset(2026, 7, 31, 11, 30, 0, TimeSpan.FromHours(-5));
        return new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"FC-{Guid.NewGuid():N}", received.AddDays(-1), received, true,
            received.AddDays(30), "cop", "Entrada E2E",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Producto E2E", 10m, 6_000m,
                10_000m, "01", 19m, PurchasingTaxTreatments.DeductibleInputVat)]);
    }

    private static HttpRequestMessage CreateMessage(
        ConfirmGoodsReceiptRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The expected SQL scalar was not returned.");
        if (value is DBNull) throw new InvalidOperationException("The expected SQL scalar is null.");
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private async Task<decimal?> ReadNullableDecimalAsync(string column)
    {
        Assert.Contains(column, new[] { "QuantityOnHand", "InventoryValue", "AverageUnitCost" });
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [{column}] FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId";
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (decimal)value;
    }

    private async Task<JobEvidence> ReadJobAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,LastError
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt';
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("The goods receipt job was not found.");
        return new JobEvidence(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<int> CountAsync(string table, Guid documentId)
    {
        var columns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InventoryMovements"] = "DocumentId",
            ["SupplierCostObservations"] = "SourceDocumentId",
            ["Payables"] = "SourceDocumentId",
            ["PayableTransactions"] = "SourceDocumentId",
            ["PriceRevisionProposals"] = "SourceDocumentId",
            ["ServerOutboxMessages"] = "DocumentId"
        };
        Assert.True(columns.TryGetValue(table, out var idColumn));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{idColumn}]=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record JobEvidence(string Status, string? LastError);
}
