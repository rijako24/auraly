using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesCheckoutTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Online_checkout_uses_the_register_series_and_processes_once()
    {
        var userId = await CreateUserAsync("checkout");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        client.Timeout = TimeSpan.FromSeconds(60);

        var draft = await OpenAsync(client);
        var captured = await CaptureAsync(client, draft);
        var command = new CompleteOnlineSalesDraftRequest(
            captured.Version,
            [new OnlineSalesPayment("Cash", captured.PayableAmount, null)]);
        var key = $"checkout-{Guid.NewGuid():N}";

        var completed = await CompleteAsync(
            client,
            captured.DraftId,
            command,
            key);

        Assert.False(completed.IsDuplicate);
        Assert.Equal(fixture.OnlineRegisterId, completed.NextDraft.RegisterId);
        Assert.NotEqual(captured.DraftId, completed.NextDraft.DraftId);
        Assert.Empty(completed.NextDraft.Lines);
        Assert.Equal("Active", completed.NextDraft.Status);
        Assert.StartsWith("VTA04-", completed.Receipt.DocumentNumber);
        Assert.StartsWith(ServerSliceFixture.Prefix, completed.Receipt.FiscalNumber);
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.Cufe));
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.QrPayload));
        Assert.Equal(captured.PayableAmount, completed.Receipt.PayableAmount);

        var persisted = await ReadPersistenceAsync(completed.Receipt.DocumentId);
        Assert.Equal(fixture.OnlineRegisterId, persisted.RegisterId);
        Assert.Null(persisted.DeviceId);
        Assert.Equal(SaleSourceModes.Online, persisted.SourceMode);
        Assert.Equal(userId, persisted.SoldByUserId);
        Assert.NotNull(persisted.WorkSessionId);
        Assert.Equal(1, persisted.DocumentCount);
        Assert.Equal(1, persisted.LineCount);
        Assert.Equal(1, persisted.PaymentCount);
        Assert.Equal(1, persisted.InventoryMovementCount);
        Assert.Equal(1, persisted.WorkSessionMovementCount);
        Assert.Equal(1, persisted.ServerOutboxCount);
        Assert.Equal(1, persisted.ProcessingJobCount);
        Assert.Equal(1, persisted.TaxSummaryCount);
        Assert.Equal("Completed", persisted.CheckoutStatus);
        Assert.Equal("Consumed", persisted.DraftStatus);

        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.OnlineRegisterId);
        using (var searchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/sales/search",
                   new SearchOnlineSalesIssuedSalesRequest(
                       context,
                       completed.Receipt.DocumentNumber,
                       0,
                       50)))
        {
            searchResponse.EnsureSuccessStatusCode();
            var page = await searchResponse.Content
                .ReadFromJsonAsync<OnlineSalesIssuedSalePage>();
            Assert.NotNull(page);
            var issued = Assert.Single(page.Items);
            Assert.Equal(completed.Receipt.DocumentId, issued.DocumentId);
            Assert.Equal(completed.Receipt.DocumentNumber, issued.DocumentNumber);
            Assert.Equal(completed.Receipt.FiscalNumber, issued.FiscalNumber);
            Assert.Equal(completed.Receipt.CustomerName, issued.CustomerName);
        }
        using (var receiptResponse = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/receipt",
                   context))
        {
            receiptResponse.EnsureSuccessStatusCode();
            var printable = await receiptResponse.Content
                .ReadFromJsonAsync<OnlineSalesReceipt>();
            Assert.NotNull(printable);
            Assert.Equal(completed.Receipt.DocumentId, printable.DocumentId);
            Assert.Equal(completed.Receipt.DocumentNumber, printable.DocumentNumber);
            Assert.Equal(completed.Receipt.FiscalNumber, printable.FiscalNumber);
            Assert.Equal(completed.Receipt.Cufe, printable.Cufe);
            Assert.Equal(completed.Receipt.QrPayload, printable.QrPayload);
            Assert.Equal(completed.Receipt.CustomerName, printable.CustomerName);
            Assert.Single(printable.Lines);
            Assert.Single(printable.Payments);
        }
        var qrUrl =
            $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/qr" +
            $"?businessId={fixture.BusinessId:D}" +
            $"&registerId={fixture.OnlineRegisterId:D}";
        using (var qrResponse = await client.GetAsync(qrUrl))
        {
            qrResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/svg+xml", qrResponse.Content.Headers.ContentType?.MediaType);
            var svg = await qrResponse.Content.ReadAsStringAsync();
            Assert.Contains("<svg", svg, StringComparison.Ordinal);
            Assert.DoesNotContain(completed.Receipt.Cufe, svg, StringComparison.Ordinal);
        }
        using (var wrongRegisterResponse = await client.GetAsync(
                   $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/qr" +
                   $"?businessId={fixture.BusinessId:D}" +
                   $"&registerId={Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongRegisterResponse.StatusCode);
        }

        var replay = await CompleteAsync(
            client,
            captured.DraftId,
            command,
            key);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(completed.Receipt.DocumentId, replay.Receipt.DocumentId);
        Assert.Equal(completed.Receipt.DocumentNumber, replay.Receipt.DocumentNumber);
        Assert.Equal(completed.Receipt.FiscalNumber, replay.Receipt.FiscalNumber);
        Assert.Equal(completed.Receipt.Cufe, replay.Receipt.Cufe);
        Assert.Equal(completed.NextDraft.DraftId, replay.NextDraft.DraftId);

        var afterReplay = await ReadPersistenceAsync(completed.Receipt.DocumentId);
        Assert.Equal(1, afterReplay.DocumentCount);
        Assert.Equal(1, afterReplay.LineCount);
        Assert.Equal(1, afterReplay.PaymentCount);
        Assert.Equal(1, afterReplay.InventoryMovementCount);
        Assert.Equal(1, afterReplay.WorkSessionMovementCount);
        Assert.Equal(1, afterReplay.ServerOutboxCount);
        Assert.Equal(1, afterReplay.ProcessingJobCount);
        Assert.Equal(1, afterReplay.TaxSummaryCount);

        using var conflictRequest = Mutation(
            captured.DraftId,
            command with
            {
                Payments =
                [
                    new OnlineSalesPayment(
                        "Transfer",
                        captured.PayableAmount,
                        "DIFFERENT")
                ]
            },
            key);
        using var conflict = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Two_online_cashiers_share_one_register_without_number_collisions()
    {
        var firstUserId = await CreateUserAsync("parallel-a");
        var secondUserId = await CreateUserAsync("parallel-b");
        using var firstClient = fixture.CreateUserClient(
            firstUserId,
            CommercePermissionCodes.SalesCreate);
        using var secondClient = fixture.CreateUserClient(
            secondUserId,
            CommercePermissionCodes.SalesCreate);
        firstClient.Timeout = TimeSpan.FromSeconds(90);
        secondClient.Timeout = TimeSpan.FromSeconds(90);

        var firstDraft = await CaptureAsync(
            firstClient,
            await OpenAsync(firstClient));
        var secondDraft = await CaptureAsync(
            secondClient,
            await OpenAsync(secondClient));
        using var firstRequest = Mutation(
            firstDraft.DraftId,
            new CompleteOnlineSalesDraftRequest(
                firstDraft.Version,
                [new OnlineSalesPayment("Cash", firstDraft.PayableAmount, null)]),
            $"parallel-a-{Guid.NewGuid():N}");
        using var secondRequest = Mutation(
            secondDraft.DraftId,
            new CompleteOnlineSalesDraftRequest(
                secondDraft.Version,
                [new OnlineSalesPayment("Cash", secondDraft.PayableAmount, null)]),
            $"parallel-b-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest),
            secondClient.SendAsync(secondRequest));
        try
        {
            foreach (var response in responses)
                response.EnsureSuccessStatusCode();
            var first = await responses[0].Content
                .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>();
            var second = await responses[1].Content
                .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>();
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first.Receipt.DocumentId, second.Receipt.DocumentId);
            Assert.NotEqual(first.Receipt.DocumentNumber, second.Receipt.DocumentNumber);
            Assert.NotEqual(first.Receipt.FiscalNumber, second.Receipt.FiscalNumber);
            Assert.NotEqual(first.Receipt.Cufe, second.Receipt.Cufe);

            var consecutives = await ReadConsecutivesAsync(
                first.Receipt.DocumentId,
                second.Receipt.DocumentId);
            Assert.Equal(2, consecutives.Count);
            Assert.All(
                consecutives,
                value => Assert.Equal(fixture.OnlineRegisterId, value.RegisterId));
            Assert.Equal(
                2,
                consecutives.Select(value => value.DocumentConsecutive)
                    .Distinct().Count());
            Assert.Equal(
                2,
                consecutives.Select(value => value.FiscalConsecutive)
                    .Distinct().Count());
            Assert.Equal(
                2,
                consecutives.Select(value => value.SoldByUserId)
                    .Distinct().Count());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Unsupported_credit_does_not_reserve_numbers()
    {
        var userId = await CreateUserAsync("credit");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var before = await ReadCursorValuesAsync();
        using var request = Mutation(
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Credit", captured.PayableAmount, null)]),
            $"credit-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadCursorValuesAsync());
    }

    [Fact]
    public async Task Active_fiscal_ranges_cannot_overlap_between_registers()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.FiscalSeries(
              SeriesId,BusinessId,RegisterId,FiscalAuthorizationId,
              DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(
              @SeriesId,@BusinessId,@RegisterId,@FiscalAuthorizationId,
              N'SalesInvoice',@Prefix,6000,7000,1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@SeriesId", Guid.NewGuid());
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@RegisterId", fixture.RegisterId);
        command.Parameters.AddWithValue(
            "@FiscalAuthorizationId",
            fixture.FiscalAuthorizationId);
        command.Parameters.AddWithValue("@Prefix", ServerSliceFixture.Prefix);

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => command.ExecuteNonQueryAsync());

        Assert.Equal(51020, exception.Number);
        Assert.Contains(
            "rangos solapados",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateUserAsync(string prefix)
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
              N'Caja',N'Online',1,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"{prefix}-{userId:N}"),
            new("@Email", $"{prefix}-{userId:N}@test.local"));
        return userId;
    }

    private async Task<OnlineSalesDraft> OpenAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.OnlineRegisterId)));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private async Task<OnlineSalesDraft> CaptureAsync(
        HttpClient client,
        OnlineSalesDraft draft)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/capture")
        {
            Content = JsonContent.Create(
                new CaptureOnlineSalesDraftProductRequest(
                    "P-E2E",
                    1m,
                    draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty capture response.");
    }

    private static async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        HttpClient client,
        Guid draftId,
        CompleteOnlineSalesDraftRequest command,
        string key)
    {
        using var request = Mutation(draftId, command, key);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>()
            ?? throw new InvalidOperationException("Empty checkout response.");
    }

    private static HttpRequestMessage Mutation(
        Guid draftId,
        CompleteOnlineSalesDraftRequest body,
        string key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/complete")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private async Task<PersistenceEvidence> ReadPersistenceAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.RegisterId,d.DeviceId,d.SourceMode,d.SoldByUserId,d.WorkSessionId,
                   (SELECT COUNT(*) FROM dbo.SalesDocuments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesDocumentLines x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesPayments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.InventoryMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.WorkSessionMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.ServerOutboxMessages x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesDocumentTaxSummaries x WHERE x.DocumentId=d.DocumentId),
                   receipt.Status,draft.Status
            FROM dbo.SalesDocuments d
            JOIN dbo.OnlineSalesCheckoutReceipts receipt
              ON receipt.DocumentId=d.DocumentId
            JOIN dbo.SalesDrafts draft
              ON draft.SalesDraftId=receipt.SalesDraftId
            WHERE d.DocumentId=@DocumentId;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetString(13),
            reader.GetString(14));
    }

    private async Task<IReadOnlyList<ConsecutiveEvidence>> ReadConsecutivesAsync(
        Guid firstDocumentId,
        Guid secondDocumentId)
    {
        var rows = new List<ConsecutiveEvidence>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RegisterId,DocumentConsecutive,FiscalConsecutive,SoldByUserId
            FROM dbo.SalesDocuments
            WHERE DocumentId IN (@First,@Second)
            ORDER BY DocumentConsecutive;
            """;
        command.Parameters.AddWithValue("@First", firstDocumentId);
        command.Parameters.AddWithValue("@Second", secondDocumentId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetGuid(3)));
        return rows;
    }

    private async Task<(long? Document, long? Fiscal)> ReadCursorValuesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT NextConsecutive FROM dbo.DocumentSeriesCursors
               WHERE DocumentSeriesId=@DocumentSeriesId),
              (SELECT NextConsecutive FROM dbo.FiscalSeriesCursors
               WHERE SeriesId=@FiscalSeriesId);
            """;
        command.Parameters.AddWithValue(
            "@DocumentSeriesId",
            fixture.OnlineDocumentSeriesId);
        command.Parameters.AddWithValue("@FiscalSeriesId", fixture.OnlineSeriesId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private async Task ExecuteAsync(
        string sql,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record PersistenceEvidence(
        Guid RegisterId,
        Guid? DeviceId,
        string SourceMode,
        Guid SoldByUserId,
        Guid? WorkSessionId,
        int DocumentCount,
        int LineCount,
        int PaymentCount,
        int InventoryMovementCount,
        int WorkSessionMovementCount,
        int ServerOutboxCount,
        int ProcessingJobCount,
        int TaxSummaryCount,
        string CheckoutStatus,
        string DraftStatus);

    private sealed record ConsecutiveEvidence(
        Guid RegisterId,
        long DocumentConsecutive,
        long FiscalConsecutive,
        Guid SoldByUserId);
}
