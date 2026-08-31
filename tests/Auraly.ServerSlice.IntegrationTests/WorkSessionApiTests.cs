using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class WorkSessionApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Concurrent_open_requests_resume_the_same_work_session()
    {
        var userId = await CreateUserAsync("work-session-concurrent");
        using var firstClient = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open);
        using var secondClient = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open);
        var command = new OpenWorkSessionRequest(
            fixture.BusinessId,
            fixture.WarehouseId,
            null);

        var requests = await Task.WhenAll(
            OpenAsync(firstClient, command),
            OpenAsync(secondClient, command));

        Assert.Equal(requests[0].WorkSessionId, requests[1].WorkSessionId);
        Assert.Equal(1, await CountOpenSessionsAsync(userId));
    }

    [Fact]
    public async Task Opening_another_context_requires_explicitly_closing_the_active_work_session()
    {
        var userId = await CreateUserAsync("work-session-context-switch");
        var otherWarehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT dbo.Warehouses
                  (WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsActive,CreatedAt)
                VALUES
                  (@WarehouseId,@BusinessId,@Code,N'Bodega cambio de contexto',0,1,SYSDATETIMEOFFSET());
                """;
            insert.Parameters.AddWithValue("@WarehouseId", otherWarehouseId);
            insert.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            insert.Parameters.AddWithValue("@Code", $"CTX-{otherWarehouseId:N}"[..16]);
            await insert.ExecuteNonQueryAsync();
        }
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open);

        var first = await OpenAsync(client, new OpenWorkSessionRequest(
            fixture.BusinessId, fixture.WarehouseId, null));
        using var conflict = await client.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(fixture.BusinessId, otherWarehouseId, null));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, await CountOpenSessionsAsync(userId));
        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT s.Status,COUNT(c.WorkSessionClosureId)
            FROM dbo.WorkSessions s
            LEFT JOIN dbo.WorkSessionClosures c ON c.WorkSessionId=s.WorkSessionId
            WHERE s.WorkSessionId=@WorkSessionId
            GROUP BY s.Status;
            """;
        verify.Parameters.AddWithValue("@WorkSessionId", first.WorkSessionId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Open", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
    }

    [Fact]
    public async Task Work_session_rejects_scopes_outside_the_authenticated_context()
    {
        var userId = await CreateUserAsync("work-session-scope");
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Open);

        using (var otherBusiness = await client.PostAsJsonAsync(
                   "/api/commerce/v1/work-sessions/current",
                   new OpenWorkSessionRequest(
                       Guid.NewGuid(), fixture.WarehouseId, null)))
            Assert.Equal(HttpStatusCode.Forbidden, otherBusiness.StatusCode);

        using (var unknownDevice = await client.PostAsJsonAsync(
                   "/api/commerce/v1/work-sessions/current",
                   new OpenWorkSessionRequest(
                       fixture.BusinessId, fixture.WarehouseId, Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.Forbidden, unknownDevice.StatusCode);

        Assert.Equal(0, await CountOpenSessionsAsync(userId));
    }

    [Fact]
    public async Task Closure_totals_are_isolated_by_cashier_work_session()
    {
        var firstUserId = await CreateUserAsync("work-session-isolation-first");
        var secondUserId = await CreateUserAsync("work-session-isolation-second");
        using var firstClient = fixture.CreateUserClient(
            firstUserId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close);
        using var secondClient = fixture.CreateUserClient(
            secondUserId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close);
        var command = new OpenWorkSessionRequest(
            fixture.BusinessId,
            fixture.WarehouseId,
            null);
        var first = await OpenAsync(firstClient, command);
        var second = await OpenAsync(secondClient, command);
        await InsertMovementsAsync(first.WorkSessionId, firstUserId);
        await InsertMovementsAsync(second.WorkSessionId, secondUserId);

        var firstPreview = await firstClient.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{first.WorkSessionId:D}/closure-preview");
        var secondPreview = await secondClient.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{second.WorkSessionId:D}/closure-preview");

        Assert.NotNull(firstPreview);
        Assert.NotNull(secondPreview);
        Assert.Equal(160_000m, firstPreview.TotalOther);
        Assert.Equal(160_000m, secondPreview.TotalOther);
        Assert.Equal(80_000m, firstPreview.ExpectedCash);
        Assert.Equal(80_000m, secondPreview.ExpectedCash);
    }

    [Fact]
    public async Task Closure_preview_reports_every_configured_payment_method_without_mixing_amounts()
    {
        var userId = await CreateUserAsync("work-session-empty-count");
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close);
        var opened = await OpenAsync(client, new OpenWorkSessionRequest(
            fixture.BusinessId,
            fixture.WarehouseId,
            null));

        using var response = await client.GetAsync(
            $"/api/commerce/v1/work-sessions/{opened.WorkSessionId:D}/closure-preview");
        Assert.True(response.IsSuccessStatusCode,
            $"Closure preview failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var preview = await response.Content.ReadFromJsonAsync<WorkSessionClosurePreviewView>();

        Assert.NotNull(preview);
        var totals = preview.PaymentTotals.ToDictionary(
            value => value.PaymentMethodCode,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Cash", totals.Keys);
        Assert.Contains("Card", totals.Keys);
        Assert.Contains("Transfer", totals.Keys);
        Assert.Equal(3, totals.Values.Count(value => value.RequiresCount));
        Assert.DoesNotContain("BankTransfer", totals.Keys);
        Assert.DoesNotContain("Deposit", totals.Keys);
        Assert.All(totals.Values, value => Assert.Equal(0m, value.NetAmount));
        Assert.Equal(0, preview.SalesCount);
        Assert.Equal(0, preview.CreditSalesCount);
        Assert.Equal(0m, preview.CreditSalesAmount);
        Assert.Equal(0, preview.ReturnCount);
    }

    [Fact]
    public async Task Cashier_is_asked_for_supervisor_approval_before_opening_the_count()
    {
        var userId = await CreateUserAsync("work-session-supervised-close");
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open);
        var opened = await OpenAsync(client, new OpenWorkSessionRequest(
            fixture.BusinessId,
            fixture.WarehouseId,
            null));

        using var preview = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/commerce/v1/work-sessions/{opened.WorkSessionId:D}/closure-preview");
        preview.Headers.Add("X-Auraly-Draft-Id", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(preview);
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Contains("ApprovalRequired", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("work-sessions.close' is required", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_session_opens_resumes_closes_and_reopens_with_immutable_totals()
    {
        var userId = await CreateUserAsync("work-session");
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close);

        using (var empty = await client.GetAsync(
                   "/api/commerce/v1/work-sessions/current"))
            Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        var command = new OpenWorkSessionRequest(
            fixture.BusinessId,
            fixture.WarehouseId,
            null);
        var opened = await OpenAsync(client, command);
        var resumed = await OpenAsync(client, command);
        Assert.Equal(opened.WorkSessionId, resumed.WorkSessionId);
        Assert.Equal(userId, opened.UserId);
        Assert.Equal(fixture.BusinessId, opened.BusinessId);
        Assert.Equal(fixture.WarehouseId, opened.WarehouseId);
        Assert.Equal("Open", opened.Status);

        using (var currentResponse = await client.GetAsync(
                   "/api/commerce/v1/work-sessions/current"))
        {
            currentResponse.EnsureSuccessStatusCode();
            var current = await currentResponse.Content
                .ReadFromJsonAsync<WorkSessionView>();
            Assert.NotNull(current);
            Assert.Equal(opened.WorkSessionId, current.WorkSessionId);
        }

        using (var denied = fixture.CreateUserClient(userId))
        using (var deniedResponse = await denied.PostAsJsonAsync(
                   "/api/commerce/v1/work-sessions/current", command))
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        await InsertMovementsAsync(opened.WorkSessionId, userId);
        using (var previewResponse = await client.GetAsync(
                   $"/api/commerce/v1/work-sessions/{opened.WorkSessionId:D}/closure-preview"))
        {
            previewResponse.EnsureSuccessStatusCode();
            var preview = await previewResponse.Content
                .ReadFromJsonAsync<WorkSessionClosurePreviewView>();
            Assert.NotNull(preview);
            Assert.Equal(opened.WorkSessionId, preview.WorkSessionId);
            Assert.Equal(80_000m, preview.ExpectedCash);
        }
        var key = $"close-{Guid.NewGuid():N}";
        var closure = await CloseAsync(
            client,
            opened.WorkSessionId,
            key,
            new CloseWorkSessionRequest(
                75_000m,
                "Faltante de efectivo verificado",
                PaymentCounts:
                [
                    new WorkSessionPaymentCount("Cash", 75_000m),
                    new WorkSessionPaymentCount("Card", 50_000m),
                    new WorkSessionPaymentCount("Transfer", 30_000m)
                ]));
        Assert.Equal(0m, closure.TotalSales);
        Assert.Equal(0m, closure.TotalRefunds);
        Assert.Equal(160_000m, closure.TotalOther);
        Assert.Equal(160_000m, closure.NetAmount);
        Assert.Equal(80_000m, closure.ExpectedCash);
        Assert.Equal(75_000m, closure.CountedCash);
        Assert.Equal(-5_000m, closure.CashDifference);
        var totals = closure.PaymentTotals.ToDictionary(
            value => value.PaymentMethodCode,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(80_000m, totals["Cash"].NetAmount);
        Assert.Equal(75_000m, totals["Cash"].CountedAmount);
        Assert.Equal(-5_000m, totals["Cash"].Difference);
        Assert.Equal(50_000m, totals["Card"].NetAmount);
        Assert.Equal(50_000m, totals["Card"].CountedAmount);
        Assert.Equal(0m, totals["Card"].Difference);
        Assert.Equal(30_000m, totals["Transfer"].NetAmount);
        Assert.Equal(30_000m, totals["Transfer"].CountedAmount);
        Assert.Equal(0m, totals["Transfer"].Difference);

        var replay = await CloseAsync(
            client,
            opened.WorkSessionId,
            key,
            new CloseWorkSessionRequest(
                75_000m,
                "Faltante de efectivo verificado",
                PaymentCounts:
                [
                    new WorkSessionPaymentCount("Cash", 75_000m),
                    new WorkSessionPaymentCount("Card", 50_000m),
                    new WorkSessionPaymentCount("Transfer", 30_000m)
                ]));
        Assert.Equal(closure.WorkSessionClosureId, replay.WorkSessionClosureId);

        using (var differentKey = CreateCloseRequest(
                   opened.WorkSessionId,
                   $"other-{Guid.NewGuid():N}",
                   new CloseWorkSessionRequest(75_000m, null)))
        using (var conflict = await client.SendAsync(differentKey))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using (var noCurrent = await client.GetAsync(
                   "/api/commerce/v1/work-sessions/current"))
            Assert.Equal(HttpStatusCode.NoContent, noCurrent.StatusCode);

        using (var receiptResponse = await client.GetAsync(
                   $"/api/commerce/v1/work-sessions/{opened.WorkSessionId:D}/closure"))
        {
            receiptResponse.EnsureSuccessStatusCode();
            var receipt = await receiptResponse.Content
                .ReadFromJsonAsync<WorkSessionClosureView>();
            Assert.NotNull(receipt);
            Assert.Equal(closure.WorkSessionClosureId, receipt.WorkSessionClosureId);
            Assert.Equal(closure.WorkSessionId, receipt.WorkSessionId);
            Assert.Equal(closure.ExpectedCash, receipt.ExpectedCash);
            Assert.Equal(closure.CashDifference, receipt.CashDifference);
        }

        var reopened = await OpenAsync(client, command);
        Assert.NotEqual(opened.WorkSessionId, reopened.WorkSessionId);
    }

    [Fact]
    public async Task Closure_reconciliation_reclassifies_a_payment_method_error_without_negative_display_amounts()
    {
        var userId = await CreateUserAsync("work-session-reconciliation");
        using var client = fixture.CreateUserClient(
            userId,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close,
            WorkSessionPermissionCodes.ReadCashDifferences,
            WorkSessionPermissionCodes.ReconcileClosures);
        var opened = await OpenAsync(client, new OpenWorkSessionRequest(
            fixture.BusinessId, fixture.WarehouseId, null));
        await InsertMovementsAsync(opened.WorkSessionId, userId);
        var closure = await CloseAsync(client, opened.WorkSessionId, $"close-{Guid.NewGuid():N}",
            new CloseWorkSessionRequest(100_000m, "Medio de pago por verificar", PaymentCounts:
            [
                new WorkSessionPaymentCount("Cash", 100_000m),
                new WorkSessionPaymentCount("Card", 50_000m),
                new WorkSessionPaymentCount("Transfer", 10_000m)
            ]));

        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var page = await client.GetFromJsonAsync<WorkSessionClosurePage>(
            $"/api/commerce/v1/work-sessions/closures?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        var listed = Assert.Single(page!.Items, item => item.WorkSessionClosureId == closure.WorkSessionClosureId);
        Assert.Equal("Pending", listed.ReconciliationStatus);
        Assert.Equal(20_000m, listed.PaymentTotals.Single(item => item.PaymentMethodCode == "Cash").Difference);
        Assert.Equal(-20_000m, listed.PaymentTotals.Single(item => item.PaymentMethodCode == "Transfer").Difference);

        var verificationItems = await client.GetFromJsonAsync<WorkSessionPaymentVerificationItem[]>(
            $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId:D}/payment-verifications");
        Assert.NotNull(verificationItems);
        Assert.Equal(4, verificationItems.Length);
        Assert.Equal(2, verificationItems.Count(item => item.PaymentMethodCode == "Card"));
        Assert.Equal(2, verificationItems.Count(item => item.PaymentMethodCode == "Transfer"));

        using (var incomplete = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId:D}/reconcile")
               {
                   Content = JsonContent.Create(new ReconcileWorkSessionClosureRequest(
                   [
                       new("Cash", 100_000m, true, null),
                       new("Card", 50_000m, true, null),
                       new("Transfer", 10_000m, true, null)
                   ], [new("Transfer", "Cash", 20_000m)], "Sin detalle de comprobantes"))
               })
        {
            incomplete.Headers.Add("Idempotency-Key", $"reconcile-incomplete-{Guid.NewGuid():N}");
            using var incompleteResponse = await client.SendAsync(incomplete);
            Assert.Equal(HttpStatusCode.BadRequest, incompleteResponse.StatusCode);
        }

        var verificationDecisions = verificationItems.Select(item =>
            new WorkSessionPaymentVerificationDecision(item.VerificationKey,
                item.PaymentMethodCode == "Transfer" && item.Amount == 20_000m ? "Missing" : "Verified")).ToArray();

        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId:D}/reconcile")
        {
            Content = JsonContent.Create(new ReconcileWorkSessionClosureRequest(
            [
                new("Cash", 100_000m, true, null),
                new("Card", 50_000m, true, null),
                new("Transfer", 10_000m, true, null)
            ], [new("Transfer", "Cash", 20_000m)], "Transferencia registrada como efectivo",
                verificationDecisions))
        };
        message.Headers.Add("Idempotency-Key", $"reconcile-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        var reconciliation = await response.Content.ReadFromJsonAsync<WorkSessionClosureReconciliationView>();
        Assert.NotNull(reconciliation);
        Assert.Equal("Reconciled", reconciliation.Status);
        Assert.Equal("NotRequired", reconciliation.AccountingStatus);
        var reclassification = Assert.Single(reconciliation.Reclassifications);
        Assert.Equal("Transfer", reclassification.FromPaymentMethodCode);
        Assert.Equal("Cash", reclassification.ToPaymentMethodCode);
        Assert.Equal(20_000m, reclassification.Amount);
    }

    private static async Task<WorkSessionView> OpenAsync(
        HttpClient client,
        OpenWorkSessionRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkSessionView>()
            ?? throw new InvalidOperationException("The work session response is empty.");
    }

    private static async Task<WorkSessionClosureView> CloseAsync(
        HttpClient client,
        Guid workSessionId,
        string key,
        CloseWorkSessionRequest request)
    {
        using var message = CreateCloseRequest(workSessionId, key, request);
        using var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkSessionClosureView>()
            ?? throw new InvalidOperationException("The closure response is empty.");
    }

    private static HttpRequestMessage CreateCloseRequest(
        Guid workSessionId,
        string key,
        CloseWorkSessionRequest request)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/work-sessions/{workSessionId:D}/close")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", key);
        return message;
    }

    private async Task<Guid> CreateUserAsync(string prefix)
    {
        var userId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
               N'Operador',N'Auraly',1,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@RoleId", fixture.RoleId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Username", $"{prefix}-{userId:N}");
        command.Parameters.AddWithValue("@Email", $"{prefix}-{userId:N}@test.local");
        await command.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task InsertMovementsAsync(Guid workSessionId, Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
               BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,
               SourceKey,OccurredAt,RecordedByUserId)
            VALUES
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashIn',N'Cash',100000,NULL,N'test:cash-in',SYSUTCDATETIME(),@UserId),
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashOut',N'Cash',-20000,NULL,N'test:cash-out',SYSUTCDATETIME(),@UserId),
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashIn',N'DebitCard',30000,NULL,N'test:debit-card',SYSUTCDATETIME(),@UserId),
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashIn',N'CreditCard',20000,NULL,N'test:credit-card',SYSUTCDATETIME(),@UserId),
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashIn',N'Transfer',10000,N'Comprobante 10.000',N'test:transfer-10',SYSUTCDATETIME(),@UserId),
              (NEWID(),@SessionId,NULL,NULL,CAST(SYSUTCDATETIME() AS date),
               N'CashIn',N'Transfer',20000,N'Comprobante 20.000',N'test:transfer-20',SYSUTCDATETIME(),@UserId);
            """;
        command.Parameters.AddWithValue("@SessionId", workSessionId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountOpenSessionsAsync(Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM dbo.WorkSessions
            WHERE UserId=@UserId AND Status=N'Open';
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
