using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosWorkSessionClosureApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Enrolled_device_can_download_cash_reasons_without_user_permissions()
    {
        using var client = fixture.CreateClient();
        using var request = DeviceRequest(
            HttpMethod.Get,
            $"/api/pos/v1/cash-movement-reasons?businessId={fixture.BusinessId:D}",
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {body}");
        var reasons = await response.Content.ReadFromJsonAsync<CashMovementReasonView[]>();
        Assert.NotNull(reasons);
        Assert.Contains(reasons, reason => reason.Direction == "In");
        Assert.Contains(reasons, reason => reason.Direction == "Out");
    }

    [Fact]
    public async Task Inactive_device_cannot_negotiate_push_or_synchronize()
    {
        var deviceId = Guid.NewGuid();
        const string secret = "inactive-device-secret-for-integration-test";
        var credential = PosDeviceCredentialHasher.Create(secret);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.EnrolledDevices
                  (DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                   CredentialIterations,IsActive,CreatedAt)
                VALUES
                  (@DeviceId,@TenantId,N'POS desenrolada',@CredentialSalt,
                   @CredentialHash,@CredentialIterations,0,SYSUTCDATETIME());
                """;
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.Add("@CredentialSalt", System.Data.SqlDbType.VarBinary, 32)
                .Value = credential.Salt;
            command.Parameters.Add("@CredentialHash", System.Data.SqlDbType.VarBinary, 32)
                .Value = credential.Hash;
            command.Parameters.AddWithValue("@CredentialIterations", credential.Iterations);
            await command.ExecuteNonQueryAsync();
        }

        using var client = fixture.CreateClient();
        using var request = DeviceRequest(
            HttpMethod.Post,
            $"/api/pos/v1/synchronization/negotiate?businessId={fixture.BusinessId:D}",
            deviceId,
            secret);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enrolled_device_transports_closure_authorized_by_a_user_not_by_the_device()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        const string secret = "closure-device-secret-for-integration-test";
        var credential = PosDeviceCredentialHasher.Create(secret);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.AppUsers
                  (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                   FirstName,LastName,IsActive,CreatedAt)
                VALUES
                  (@UserId,@TenantId,CONCAT(N'closure-',@UserId),UPPER(CONCAT(N'closure-',@UserId)),
                   CONCAT(@UserId,N'@test.local'),UPPER(CONCAT(@UserId,N'@test.local')),
                   N'Closure',N'Cashier',1,SYSUTCDATETIME());

                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,IsActive,CreatedAt)
                VALUES(@RoleId,@TenantId,N'Administrador de cierre',
                       UPPER(CONCAT(N'CLOSURE-',@RoleId)),1,SYSUTCDATETIME());
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
                VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
                FROM dbo.Permissions WHERE Resource=N'work-sessions.close';

                INSERT dbo.EnrolledDevices
                  (DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                   CredentialIterations,IsActive,CreatedAt)
                VALUES
                  (@DeviceId,@TenantId,N'POS cierre autorizado',
                   @CredentialSalt,@CredentialHash,@CredentialIterations,1,SYSUTCDATETIME());

                INSERT dbo.WorkSessions
                  (WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                   OpenedAt,LastActivityAt,Status)
                VALUES
                  (@WorkSessionId,@BusinessId,@WarehouseId,@UserId,@DeviceId,
                   SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),N'Open');
                """;
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            command.Parameters.AddWithValue("@RoleId", roleId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            command.Parameters.Add("@CredentialSalt", System.Data.SqlDbType.VarBinary, 32)
                .Value = credential.Salt;
            command.Parameters.Add("@CredentialHash", System.Data.SqlDbType.VarBinary, 32)
                .Value = credential.Hash;
            command.Parameters.AddWithValue("@CredentialIterations", credential.Iterations);
            await command.ExecuteNonQueryAsync();
        }

        using var client = fixture.CreateClient();
        using var previewRequest = DeviceRequest(
            HttpMethod.Get,
            $"/api/pos/v1/work-sessions/{workSessionId:D}/closure-preview?userId={userId:D}",
            deviceId,
            secret);
        using var previewResponse = await client.SendAsync(previewRequest);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<WorkSessionClosurePreviewView>();
        Assert.NotNull(preview);
        Assert.Equal(workSessionId, preview.WorkSessionId);

        var operationId = Guid.NewGuid();
        using var closeRequest = DeviceRequest(
            HttpMethod.Post,
            $"/api/pos/v1/work-sessions/{workSessionId:D}/close",
            deviceId,
            secret);
        closeRequest.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        closeRequest.Content = JsonContent.Create(new DeviceCloseWorkSessionRequest(
            userId,
            workSessionId,
            0m,
            "Cierre transportado por caja enrolada",
            userId,
            [
                new WorkSessionPaymentCount("Cash", 0m),
                new WorkSessionPaymentCount("Card", 0m),
                new WorkSessionPaymentCount("Transfer", 0m)
            ]));
        using var closeResponse = await client.SendAsync(closeRequest);
        var closeBody = await closeResponse.Content.ReadAsStringAsync();
        Assert.True(
            closeResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)closeResponse.StatusCode}: {closeBody}");
        var closure = await closeResponse.Content.ReadFromJsonAsync<WorkSessionClosureView>();
        Assert.NotNull(closure);
        Assert.Equal(workSessionId, closure.WorkSessionId);
        Assert.Equal(userId, closure.UserId);

        using var replayLookup = DeviceRequest(
            HttpMethod.Get,
            $"/api/pos/v1/work-sessions/{workSessionId:D}/closure?userId={userId:D}",
            deviceId,
            secret);
        using var replayResponse = await client.SendAsync(replayLookup);
        var replayBody = await replayResponse.Content.ReadAsStringAsync();
        Assert.True(
            replayResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)replayResponse.StatusCode}: {replayBody}");
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<WorkSessionClosureView>();
        Assert.NotNull(replay);
        Assert.Equal(closure.WorkSessionClosureId, replay.WorkSessionClosureId);

        await using var verificationConnection = new SqlConnection(fixture.ConnectionString);
        await verificationConnection.OpenAsync();
        await using var verification = verificationConnection.CreateCommand();
        verification.CommandText = """
            SELECT ClosedByUserId
            FROM dbo.WorkSessionClosures
            WHERE WorkSessionId=@WorkSessionId;
            """;
        verification.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        Assert.Equal(userId, (Guid)(await verification.ExecuteScalarAsync())!);
    }

    private static HttpRequestMessage DeviceRequest(
        HttpMethod method,
        string path,
        Guid deviceId,
        string secret)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", secret);
        return request;
    }
}
