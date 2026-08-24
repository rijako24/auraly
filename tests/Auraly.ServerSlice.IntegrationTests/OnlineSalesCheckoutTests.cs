using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesCheckoutTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Online_checkout_uses_the_server_series_and_processes_once()
    {
        var userId = await CreateUserAsync("checkout");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
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
        Assert.Equal(captured.WorkSessionId, completed.NextDraft.WorkSessionId);
        Assert.NotEqual(captured.DraftId, completed.NextDraft.DraftId);
        Assert.Empty(completed.NextDraft.Lines);
        Assert.Equal("Active", completed.NextDraft.Status);
        Assert.StartsWith("VTA00-", completed.Receipt.DocumentNumber);
        Assert.StartsWith(ServerSliceFixture.Prefix, completed.Receipt.FiscalNumber);
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.Cufe));
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.QrPayload));
        Assert.Equal(captured.PayableAmount, completed.Receipt.PayableAmount);

        var persisted = await ReadPersistenceAsync(completed.Receipt.DocumentId);
        Assert.Null(persisted.DeviceId);
        Assert.NotNull(persisted.WorkSessionId);
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
        Assert.Equal("Completed", persisted.CheckoutStatus);
        Assert.Equal("Consumed", persisted.DraftStatus);

        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            captured.WorkSessionId);
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
            $"&warehouseId={fixture.WarehouseId:D}" +
            $"&workSessionId={captured.WorkSessionId:D}";
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
                   $"&warehouseId={fixture.WarehouseId:D}" +
                   $"&workSessionId={Guid.NewGuid():D}"))
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
    public async Task Commercial_receipt_uses_its_own_series_and_skips_the_fiscal_stage()
    {
        var userId = await CreateUserAsync("receipt");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
        client.Timeout = TimeSpan.FromSeconds(60);

        var captured = await CaptureAsync(client, await OpenAsync(client));
        var command = new CompleteOnlineSalesDraftRequest(
            captured.Version,
            [new OnlineSalesPayment("Cash", captured.PayableAmount, null)],
            DocumentType: PosSaleDocumentTypes.Receipt);
        var key = $"receipt-{Guid.NewGuid():N}";

        var completed = await CompleteAsync(client, captured.DraftId, command, key);

        Assert.Equal(PosSaleDocumentTypes.Receipt, completed.Receipt.DocumentType);
        Assert.StartsWith("CVI00-", completed.Receipt.DocumentNumber);
        Assert.Null(completed.Receipt.FiscalNumber);
        Assert.Null(completed.Receipt.Cufe);
        Assert.Null(completed.Receipt.QrPayload);
        Assert.Equal("CommercialAccepted", completed.Receipt.FiscalStatus);
        Assert.Equal(captured.PayableAmount, completed.Receipt.PayableAmount);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var sql = connection.CreateCommand();
            sql.CommandText = """
                SELECT d.DocumentType,d.FiscalSeriesId,d.FiscalNumber,d.CufeReceived,d.FiscalStatus,
                       (SELECT COUNT(*) FROM dbo.FiscalSnapshots f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.FiscalDocuments f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.InventoryMovements m WHERE m.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.SalesPayments p WHERE p.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.ServerOutboxMessages o WHERE o.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.Receivables r WHERE r.SourceDocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.AccountingPostingJobs a WHERE a.SourceDocumentId=d.DocumentId)
                FROM dbo.SalesDocuments d WHERE d.DocumentId=@DocumentId;
                """;
            sql.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            await using var reader = await sql.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(PosSaleDocumentTypes.Receipt, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
            Assert.True(reader.IsDBNull(4));
            Assert.Equal(0, reader.GetInt32(5));
            Assert.Equal(0, reader.GetInt32(6));
            Assert.Equal(0, reader.GetInt32(7));
            Assert.Equal(1, reader.GetInt32(8));
            Assert.Equal(1, reader.GetInt32(9));
            Assert.Equal(1, reader.GetInt32(10));
            Assert.Equal(0, reader.GetInt32(11));
            Assert.Equal(1, reader.GetInt32(12));
        }

        var replay = await CompleteAsync(client, captured.DraftId, command, key);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(completed.Receipt.DocumentId, replay.Receipt.DocumentId);
        Assert.Equal(completed.Receipt.DocumentNumber, replay.Receipt.DocumentNumber);

        await using var verify = new SqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*),
                   (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@DocumentId)
            FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId;
            """;
        count.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
        await using var counts = await count.ExecuteReaderAsync();
        Assert.True(await counts.ReadAsync());
        Assert.Equal(1, counts.GetInt32(0));
        Assert.Equal(1, counts.GetInt32(1));
        Assert.Equal(1, counts.GetInt32(2));
    }

    [Fact]
    public async Task Customer_configured_for_electronic_invoicing_cannot_be_completed_as_a_commercial_receipt()
    {
        var userId = await CreateUserAsync("required-invoice");
        var customerId = await CreateElectronicInvoiceCustomerAsync(userId);
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);

        var draft = await OpenAsync(client);
        using (var select = new HttpRequestMessage(
                   HttpMethod.Put,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
               {
                   Content = JsonContent.Create(
                       new SelectOnlineSalesDraftCustomerRequest(customerId, draft.Version))
               })
        {
            select.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var selectedResponse = await client.SendAsync(select);
            selectedResponse.EnsureSuccessStatusCode();
            var selection = await selectedResponse.Content
                .ReadFromJsonAsync<OnlineSalesCustomerSelection>();
            Assert.NotNull(selection);
            Assert.True(selection.Customer?.RequiresElectronicInvoice);
            draft = selection.Draft;
        }

        var captured = await CaptureAsync(client, draft);
        using var request = Mutation(
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Cash", captured.PayableAmount, null)],
                DocumentType: PosSaleDocumentTypes.Receipt),
            $"required-invoice-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("factura electronica", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountCustomerDocumentsAsync(customerId));
    }



    [Fact]
    public async Task Two_online_users_share_server_series_without_number_collisions()
    {
        var firstUserId = await CreateUserAsync("parallel-a");
        var secondUserId = await CreateUserAsync("parallel-b");
        using var firstClient = fixture.CreateUserClient(
            firstUserId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
        using var secondClient = fixture.CreateUserClient(
            secondUserId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
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
                value => Assert.Null(value.DeviceId));
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
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
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
              N'Venta',N'Online',1,SYSDATETIMEOFFSET());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(@UserRoleId,@UserId,@RoleId,@BusinessId,SYSDATETIMEOFFSET());


            """,
            new("@UserId", userId),
            new("@UserRoleId", Guid.NewGuid()),
            new("@TenantId", fixture.TenantId),
            new("@RoleId", fixture.RoleId),
            new("@BusinessId", fixture.BusinessId),
            new("@Username", $"{prefix}-{userId:N}"),
            new("@Email", $"{prefix}-{userId:N}@test.local"));
        return userId;
    }

    private async Task<Guid> CreateElectronicInvoiceCustomerAsync(Guid userId)
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,LegalName,
              CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'Organization',N'Cliente factura requerida',
              N'Cliente factura requerida',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
              IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,1,1,@UserId,SYSDATETIMEOFFSET());
            """,
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId),
            new("@UserId", userId));
        return customerId;
    }

    private async Task<int> CountCustomerDocumentsAsync(Guid customerId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.SalesDocuments WHERE CustomerId=@CustomerId;",
            connection);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<OnlineSalesDraft> OpenAsync(HttpClient client)
    {
        using var workSessionResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(
                fixture.BusinessId,
                fixture.WarehouseId,
                null));
        workSessionResponse.EnsureSuccessStatusCode();
        var workSession = await workSessionResponse.Content
            .ReadFromJsonAsync<WorkSessionView>()
            ?? throw new InvalidOperationException("Empty work session response.");

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                workSession.WorkSessionId)));
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
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items")
        {
            Content = JsonContent.Create(
                new AddOnlineSalesDraftItemRequest("P-E2E",
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
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Checkout failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
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
            SELECT d.DeviceId,d.CreatedByDeviceId,d.SourceMode,d.SoldByUserId,d.WorkSessionId,
                   (SELECT COUNT(*) FROM dbo.SalesDocuments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesDocumentLines x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesPayments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.InventoryMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.WorkSessionMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.ServerOutboxMessages x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs x WHERE x.DocumentId=d.DocumentId),
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
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
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
            reader.GetString(12),
            reader.GetString(13));
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
            SELECT DeviceId,DocumentConsecutive,FiscalConsecutive,SoldByUserId
            FROM dbo.SalesDocuments
            WHERE DocumentId IN (@First,@Second)
            ORDER BY DocumentConsecutive;
            """;
        command.Parameters.AddWithValue("@First", firstDocumentId);
        command.Parameters.AddWithValue("@Second", secondDocumentId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
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
        Guid? DeviceId,
        Guid? CreatedByDeviceId,
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
        string CheckoutStatus,
        string DraftStatus);

    private sealed record ConsecutiveEvidence(
        Guid? DeviceId,
        long DocumentConsecutive,
        long FiscalConsecutive,
        Guid SoldByUserId);
}
