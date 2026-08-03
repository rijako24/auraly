using System.Data;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ReceivablesVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Credit_sale_and_customer_payment_are_scoped_idempotent_and_accounted_once()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open,
            ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment);
        client.Timeout = TimeSpan.FromSeconds(60);

        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
        {
            response.EnsureSuccessStatusCode();
            var profile = await response.Content.ReadFromJsonAsync<CustomerCreditProfile>();
            Assert.NotNull(profile);
            Assert.Equal(500_000m, profile.AvailableCredit);
        }

        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenDraftAsync(client, workSession.WorkSessionId);
        draft = await CaptureAsync(client, draft);
        var selection = await SelectCustomerAsync(client, draft, customerId);
        var dueDate = DateTimeOffset.UtcNow.AddDays(30);
        var checkoutKey = $"receivable-sale-{Guid.NewGuid():N}";
        var checkout = await CompleteAsync(client, selection.Draft,
            new CompleteOnlineSalesDraftRequest(
                selection.Draft.Version,
                [],
                new OnlineSalesCreditTerms(selection.Draft.PayableAmount, dueDate)),
            checkoutKey);

        var receivable = await ReadReceivableAsync(checkout.Receipt.DocumentId);
        Assert.Equal(customerId, receivable.CustomerId);
        Assert.Equal(checkout.Receipt.PayableAmount, receivable.OriginalAmount);
        Assert.Equal(checkout.Receipt.PayableAmount, receivable.OutstandingAmount);
        Assert.Equal("Open", receivable.Status);
        Assert.Equal(0, await CountAsync(
            "SalesPayments", "DocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(checkout.Receipt.PayableAmount,
            await AccountAmountAsync(checkout.Receipt.DocumentId, "130505", true));

        using (var response = await client.GetAsync(
                   $"/api/commerce/v1/receivables/{receivable.ReceivableId:D}"))
        {
            response.EnsureSuccessStatusCode();
            var detail = await response.Content.ReadFromJsonAsync<ReceivableDetail>();
            Assert.NotNull(detail);
            Assert.Equal(checkout.Receipt.DocumentNumber, detail.DocumentNumber);
            Assert.Equal("Opening", Assert.Single(detail.Transactions).Type);
        }

        var partialAmount = decimal.Round(receivable.OriginalAmount * 0.4m, 4);
        var payment = new ConfirmCustomerPaymentRequest(
            Guid.NewGuid(), fixture.BusinessId, customerId,
            workSession.WorkSessionId, DateTimeOffset.UtcNow, "COP",
            CustomerPaymentMethods.Cash, null, "Abono E2E",
            [new CustomerPaymentAllocationRequest(receivable.ReceivableId, partialAmount)]);
        var paymentKey = $"receivable-payment-{payment.PaymentId:N}";
        CustomerPaymentAcceptance acceptance;
        using (var response = await SendAsync(client,
                   "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            acceptance = await response.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>()
                ?? throw new InvalidOperationException("Empty payment acceptance.");
            Assert.StartsWith("RCC", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.CustomerPayments WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(receivable.OriginalAmount - partialAmount, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
        Assert.Equal("PartiallyPaid", await ScalarAsync<string>(
            "SELECT Status FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
        Assert.Equal(1, await CountAsync(
            "CustomerPaymentApplications", "PaymentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "ServerOutboxMessages", "DocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'receivable-payment:',CONVERT(nvarchar(36),@Id))",
            payment.PaymentId));
        Assert.Equal(partialAmount,
            await AccountAmountAsync(payment.PaymentId, "110505", true));
        Assert.Equal(partialAmount,
            await AccountAmountAsync(payment.PaymentId, "130505", false));

        using (var response = await SendAsync(client,
                   "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var replay = await response.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(acceptance.MovementId, replay.MovementId);
        }
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", payment.PaymentId));

        var remaining = receivable.OriginalAmount - partialAmount;
        var concurrentAmount = decimal.Round(remaining * 0.75m, 4);
        var first = payment with
        {
            PaymentId = Guid.NewGuid(),
            Allocations = [new(receivable.ReceivableId, concurrentAmount)]
        };
        var second = first with { PaymentId = Guid.NewGuid() };
        var responses = await Task.WhenAll(
            SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", first,
                $"concurrent-{first.PaymentId:N}"),
            SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", second,
                $"concurrent-{second.PaymentId:N}"));
        try
        {
            Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.Accepted));
            Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(remaining - concurrentAmount, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
    }

    [Fact]
    public async Task Receivables_endpoints_enforce_permissions_and_business_scope()
    {
        using var denied = fixture.CreateAdminClient();
        using var list = await denied.GetAsync(
            "/api/commerce/v1/receivables?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var scoped = fixture.CreateAdminClient(
            ReceivablesPermissionCodes.RegisterPayment);
        var payment = new ConfirmCustomerPaymentRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, "COP", CustomerPaymentMethods.Cash,
            null, null, [new(Guid.NewGuid(), 1m)]);
        using var response = await SendAsync(scoped,
            "/api/commerce/v1/receivable-payments/confirm", payment,
            $"wrong-business-{payment.PaymentId:N}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(Guid CustomerId, Guid UserId)> ConfigureAsync()
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.AppUsers(
                  UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                  FirstName,LastName,IsActive,CreatedAt)
                VALUES(@UserId,@TenantId,@Username,UPPER(@Username),@Email,
                  UPPER(@Email),N'Cajero',N'Cartera',1,SYSDATETIMEOFFSET());

                INSERT dbo.Parties(
                  PartyId,TenantId,PartyType,IdentificationTypeCode,Identification,
                  NormalizedIdentification,DisplayName,LegalName,CompletionStatus,
                  IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'Organization',NULL,NULL,NULL,
                  N'Cliente crédito E2E',N'Cliente crédito E2E',N'Incomplete',1,
                  @UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Customers(
                  CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries
                    WHERE BusinessId=@BusinessId AND DocumentType=N'ReceivablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries(
                    DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'ReceivablePayment',N'RCC',N'00',
                    8,1,99999999,0,1,SYSDATETIMEOFFSET());
                """,
                new("@PartyId", partyId),
                new("@CustomerId", customerId),
                new("@TenantId", fixture.TenantId),
                new("@BusinessId", fixture.BusinessId),
                new("@UserId", userId),
                new("@Username", $"receivables-{userId:N}"),
                new("@Email", $"receivables-{userId:N}@test.local"));

            await EnsureAccountAsync(connection, transaction, "110505", "Caja general", "Asset", false);
            await EnsureAccountAsync(connection, transaction, "130505", "Clientes", "Asset", true);
            await EnsureAccountAsync(connection, transaction, "143505", "Inventarios", "Asset", false);
            await EnsureAccountAsync(connection, transaction, "240805", "IVA generado", "Liability", false);
            await EnsureAccountAsync(connection, transaction, "413595", "Ingresos", "Revenue", false);
            await EnsureAccountAsync(connection, transaction, "613595", "Costo de ventas", "Expense", false);
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Cash, "110505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.AccountsReceivable, "130505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Inventory, "143505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.OutputVat, "240805");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SalesRevenue, "413595");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.CostOfGoodsSold, "613595");
            await ExecuteAsync(connection, transaction, """
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                    WHERE TenantId=@TenantId AND Status=N'Open'
                      AND StartsOn<='2026-08-03' AND EndsOn>='2026-08-03')
                  INSERT dbo.AccountingPeriods(
                    PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                  VALUES(NEWID(),@TenantId,N'Periodo Receivables 2026',
                    '2026-01-01','2026-12-31',N'Open',SYSDATETIMEOFFSET());
                """, new SqlParameter("@TenantId", fixture.TenantId));
            await transaction.CommitAsync();
            return (customerId, userId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<OnlineSalesDraft> OpenDraftAsync(
        HttpClient client, Guid workSessionId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId, fixture.WarehouseId, workSessionId)));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private async Task<OnlineSalesDraft> CaptureAsync(
        HttpClient client, OnlineSalesDraft draft)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/capture")
        {
            Content = JsonContent.Create(new CaptureOnlineSalesDraftProductRequest(
                "P-E2E", 1m, draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty capture response.");
    }

    private static async Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        HttpClient client, OnlineSalesDraft draft, Guid customerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
        {
            Content = JsonContent.Create(new SelectOnlineSalesDraftCustomerRequest(
                customerId, draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesCustomerSelection>()
            ?? throw new InvalidOperationException("Empty customer selection response.");
    }

    private static async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        HttpClient client, OnlineSalesDraft draft,
        CompleteOnlineSalesDraftRequest command, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/complete")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>()
            ?? throw new InvalidOperationException("Empty checkout response.");
    }

    private async Task<ReceivableEvidence> ReadReceivableAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT r.ReceivableId,r.CustomerId,r.OriginalAmount,
                   r.OutstandingAmount,r.Status,d.CreditAmount,
                   d.ProcessingStatus,j.Status,j.LastError
            FROM dbo.SalesDocuments d
            LEFT JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=d.DocumentId AND j.DocumentType=d.DocumentType
            LEFT JOIN dbo.Receivables r ON r.SourceDocumentId=d.DocumentId
            WHERE d.DocumentId=@Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        if (reader.IsDBNull(0))
            throw new InvalidOperationException(
                $"Receivable missing. Credit={reader.GetDecimal(5)}; document={reader.GetString(6)}; job={(reader.IsDBNull(7) ? "none" : reader.GetString(7))}; error={(reader.IsDBNull(8) ? "none" : reader.GetString(8))}");
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetString(4));
    }

    private async Task EnsureAccountAsync(SqlConnection connection,
        SqlTransaction transaction, string code, string name, string type,
        bool requiresParty)
    {
        await ExecuteAsync(connection, transaction, """
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts
                WHERE TenantId=@TenantId AND Code=@Code)
              INSERT dbo.AccountingAccounts(
                AccountId,TenantId,Code,Name,AccountType,AllowsPosting,
                RequiresParty,IsActive,CreatedAt)
              VALUES(NEWID(),@TenantId,@Code,@Name,@Type,1,
                @RequiresParty,1,SYSDATETIMEOFFSET());
            """,
            new("@TenantId", fixture.TenantId), new("@Code", code),
            new("@Name", name), new("@Type", type),
            new("@RequiresParty", requiresParty));
    }

    private async Task EnsureMappingAsync(SqlConnection connection,
        SqlTransaction transaction, string category, string code)
    {
        await ExecuteAsync(connection, transaction, """
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND BusinessId IS NULL
                  AND Category=@Category AND EffectiveFrom='2026-01-01')
              INSERT dbo.AccountingAccountMappings(
                MappingId,TenantId,BusinessId,Category,AccountId,
                EffectiveFrom,CreatedAt)
              SELECT NEWID(),@TenantId,NULL,@Category,AccountId,
                '2026-01-01',SYSDATETIMEOFFSET()
              FROM dbo.AccountingAccounts
              WHERE TenantId=@TenantId AND Code=@Code;
            """, new("@TenantId", fixture.TenantId),
            new("@Category", category), new("@Code", code));
    }

    private static async Task ExecuteAsync(SqlConnection connection,
        SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client, string url, T body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private Task<int> CountAsync(string table, string column, Guid id)
    {
        Assert.Contains($"{table}:{column}", new[]
        {
            "SalesPayments:DocumentId",
            "ReceivableTransactions:SourceDocumentId",
            "AccountingEntries:SourceDocumentId",
            "CustomerPaymentApplications:PaymentId",
            "ServerOutboxMessages:DocumentId"
        });
        return ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", id);
    }

    private async Task<decimal> AccountAmountAsync(
        Guid documentId, string code, bool debit)
    {
        Assert.Contains(code, new[] { "110505", "130505" });
        var column = debit ? "Debit" : "Credit";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT COALESCE(SUM(l.[{column}]),0)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.SourceDocumentId=@Id AND a.Code=@Code;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Code", code);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private sealed record ReceivableEvidence(
        Guid ReceivableId, Guid CustomerId, decimal OriginalAmount,
        decimal OutstandingAmount, string Status);
}
