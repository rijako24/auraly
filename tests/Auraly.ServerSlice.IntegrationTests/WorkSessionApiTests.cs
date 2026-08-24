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
    public async Task Closure_preview_always_requests_blind_cash_and_consolidated_card_counts()
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
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<WorkSessionClosurePreviewView>();

        Assert.NotNull(preview);
        Assert.Collection(
            preview.PaymentTotals,
            card =>
            {
                Assert.Equal("Card", card.PaymentMethodCode);
                Assert.Equal(0m, card.NetAmount);
            },
            cash =>
            {
                Assert.Equal("Cash", cash.PaymentMethodCode);
                Assert.Equal(0m, cash.NetAmount);
            });
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
                    new WorkSessionPaymentCount("Card", 50_000m),
                    new WorkSessionPaymentCount("Cash", 75_000m)
                ]));
        Assert.Equal(0m, closure.TotalSales);
        Assert.Equal(0m, closure.TotalRefunds);
        Assert.Equal(160_000m, closure.TotalOther);
        Assert.Equal(160_000m, closure.NetAmount);
        Assert.Equal(80_000m, closure.ExpectedCash);
        Assert.Equal(75_000m, closure.CountedCash);
        Assert.Equal(-5_000m, closure.CashDifference);
        Assert.Collection(
            closure.PaymentTotals,
            card =>
            {
                Assert.Equal("Card", card.PaymentMethodCode);
                Assert.Equal(50_000m, card.NetAmount);
                Assert.Equal(50_000m, card.CountedAmount);
                Assert.Equal(0m, card.Difference);
            },
            cash =>
            {
                Assert.Equal("Cash", cash.PaymentMethodCode);
                Assert.Equal(80_000m, cash.NetAmount);
                Assert.Equal(75_000m, cash.CountedAmount);
                Assert.Equal(-5_000m, cash.Difference);
            },
            transfer =>
            {
                Assert.Equal("Transfer", transfer.PaymentMethodCode);
                Assert.Equal(30_000m, transfer.NetAmount);
                Assert.Null(transfer.CountedAmount);
                Assert.Null(transfer.Difference);
            });

        var replay = await CloseAsync(
            client,
            opened.WorkSessionId,
            key,
            new CloseWorkSessionRequest(
                75_000m,
                "Faltante de efectivo verificado",
                PaymentCounts:
                [
                    new WorkSessionPaymentCount("Card", 50_000m),
                    new WorkSessionPaymentCount("Cash", 75_000m)
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
               N'CashIn',N'Transfer',30000,NULL,N'test:transfer',SYSUTCDATETIME(),@UserId);
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
