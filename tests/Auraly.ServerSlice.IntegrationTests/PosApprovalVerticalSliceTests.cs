using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Authorization;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosApprovalVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Remote_approval_is_scoped_pushed_consumed_once_and_synced_offline()
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        fixture.DrainSynchronizationMessages();
        using var requester = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRemoveLine,
            CommercePermissionCodes.SalesRestartDraft,
            CommercePermissionCodes.SalesDiscount,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.PosApprovalsAuthorize,
            CommercePermissionCodes.PosApprovalsManageCredential);

        using var configured = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest("Supervisor-Secondary-1"));
        Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);

        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        using var createMessage = new HttpRequestMessage(HttpMethod.Post, "/api/pos/v1/approvals/")
        {
            Content = JsonContent.Create(new CreatePosApprovalRequest(
                fixture.BusinessId,
                fixture.DeviceId,
                fixture.WorkSessionId,
                draftId,
                lineId,
                CommercePermissionCodes.SalesRemoveLine,
                "{\"action\":\"RemoveLine\",\"product\":\"Producto de prueba\"}"))
        };
        createMessage.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        createMessage.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        createMessage.Headers.Add("X-Auraly-User-Id", fixture.UserId.ToString("D"));
        createMessage.Headers.Add("X-Auraly-Work-Session-Id", fixture.WorkSessionId.ToString("D"));
        using var deviceCreationClient = fixture.CreateClient();
        using var createdResponse = await deviceCreationClient.SendAsync(createMessage);
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<PosApprovalRequestView>();
        Assert.NotNull(created);
        Assert.Equal(PosApprovalStatus.Pending, created.Status);

        var createPush = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal(PosSynchronizationStreams.Approvals, createPush.Stream);
        Assert.Equal(fixture.BusinessId, createPush.BusinessId);

        using var stranger = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.SalesCreate);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await stranger.GetAsync(
                $"/api/commerce/v1/pos/approvals/{created.ApprovalRequestId:D}"))
            .StatusCode);

        using var selfWithElevatedToken = fixture.CreateAdminClient(
            CommercePermissionCodes.PosApprovalsAuthorize,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.SalesRemoveLine);
        using var selfDecision = await selfWithElevatedToken.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{created.ApprovalRequestId:D}/decision",
            new DecidePosApprovalRequest(true));
        Assert.Equal(HttpStatusCode.Forbidden, selfDecision.StatusCode);

        using var decision = await supervisor.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{created.ApprovalRequestId:D}/decision",
            new DecidePosApprovalRequest(true));
        decision.EnsureSuccessStatusCode();
        var decisionPush = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal(PosSynchronizationStreams.Approvals, decisionPush.Stream);

        var operationId = Guid.NewGuid();
        using var device = fixture.CreateClient();
        using var reserve = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/approvals/{created.ApprovalRequestId:D}/reserve")
        {
            Content = JsonContent.Create(new ReservePosApprovalForDeviceRequest(
                fixture.BusinessId,
                fixture.UserId,
                fixture.WorkSessionId,
                draftId,
                lineId,
                CommercePermissionCodes.SalesRemoveLine,
                operationId))
        };
        reserve.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        reserve.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var reservedResponse = await device.SendAsync(reserve);
        reservedResponse.EnsureSuccessStatusCode();
        var reserved = await reservedResponse.Content.ReadFromJsonAsync<PosApprovalDeviceReservation>();
        Assert.NotNull(reserved);
        Assert.Equal(supervisorId, reserved.AuthorizedByUserId);

        var effectCount = 1;
        Assert.Equal(1, effectCount);

        using var complete = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/approvals/{created.ApprovalRequestId:D}/complete")
        {
            Content = JsonContent.Create(new CompletePosApprovalForDeviceRequest(
                fixture.BusinessId,
                fixture.UserId,
                operationId))
        };
        complete.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        complete.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var completedResponse = await device.SendAsync(complete);
        Assert.Equal(HttpStatusCode.NoContent, completedResponse.StatusCode);

        using var invalidReuse = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/approvals/{created.ApprovalRequestId:D}/reserve")
        {
            Content = JsonContent.Create(new ReservePosApprovalForDeviceRequest(
                fixture.BusinessId,
                fixture.UserId,
                fixture.WorkSessionId,
                draftId,
                lineId,
                CommercePermissionCodes.SalesRemoveLine,
                Guid.NewGuid()))
        };
        invalidReuse.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        invalidReuse.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var invalidReuseResponse = await device.SendAsync(invalidReuse);
        Assert.Equal(HttpStatusCode.Conflict, invalidReuseResponse.StatusCode);
        Assert.Equal(1, effectCount);

        using var snapshotRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId:D}");
        snapshotRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        snapshotRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var snapshotResponse = await fixture.CreateClient().SendAsync(snapshotRequest);
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<PosOfflineIdentitySnapshot>();
        var supervisorProjection = Assert.Single(snapshot!.Users, item => item.UserId == supervisorId);
        Assert.NotNull(supervisorProjection.SupervisorCredential);
        Assert.DoesNotContain("Supervisor-Secondary-1", await snapshotResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_server_authorization_uses_secondary_secret_without_leaking_it()
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        using var requester = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesDiscount,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.PosApprovalsAuthorize,
            CommercePermissionCodes.PosApprovalsManageCredential);
        using var configured = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest("Local-Secondary-2"));
        configured.EnsureSuccessStatusCode();
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var created = await (await requester.PostAsJsonAsync(
            "/api/commerce/v1/pos/approvals/",
            new CreatePosApprovalRequest(
                fixture.BusinessId, null, fixture.WorkSessionId,
                draftId, lineId, CommercePermissionCodes.SalesDiscount,
                "{\"action\":\"Discount\"}")))
            .Content.ReadFromJsonAsync<PosApprovalRequestView>();
        Assert.NotNull(created);

        using var wrong = await requester.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{created.ApprovalRequestId:D}/local-authorization",
            new AuthorizePosApprovalLocallyRequest("wrong"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var accepted = await requester.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{created.ApprovalRequestId:D}/local-authorization",
            new AuthorizePosApprovalLocallyRequest("Local-Secondary-2"));
        accepted.EnsureSuccessStatusCode();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,DecisionMethod,DecidedByUserId
            FROM dbo.PosApprovalRequests WHERE ApprovalRequestId=@Id;
            """;
        command.Parameters.AddWithValue("@Id", created.ApprovalRequestId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(PosApprovalStatus.Approved, reader.GetString(0));
        Assert.Equal("LocalSecret", reader.GetString(1));
        Assert.Equal(supervisorId, reader.GetGuid(2));
    }

    private async Task SeedSupervisorAsync(Guid userId)
    {
        var roleId = Guid.NewGuid();
        var verifier = PosOfflinePasswordHasher.Hash(
            "Supervisor-Login-Password-1", DateTimeOffset.UtcNow);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt,
              PosOfflinePasswordSalt,PosOfflinePasswordHash,
              PosOfflinePasswordIterations,PosOfflinePasswordChangedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
              N'Supervisora',N'POS',1,SYSUTCDATETIME(),
              @Salt,@Hash,@Iterations,@ChangedAt);
            INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,IsActive,CreatedAt)
            VALUES(@RoleId,@TenantId,N'Supervisor POS',UPPER(CONCAT(N'SUPERVISOR-',@RoleId)),1,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions WHERE Resource IN(
              N'sales.create',N'sales.discount',N'sales.lines.remove',N'sales.drafts.restart',
              N'pos.approvals.read',N'pos.approvals.authorize',N'pos.approvals.manage_credential');
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@Username", $"supervisor-{userId:N}");
        command.Parameters.AddWithValue("@Email", $"supervisor-{userId:N}@test.local");
        command.Parameters.AddWithValue("@Salt", verifier.Salt);
        command.Parameters.AddWithValue("@Hash", verifier.Hash);
        command.Parameters.AddWithValue("@Iterations", verifier.Iterations);
        command.Parameters.AddWithValue("@ChangedAt", verifier.ChangedAt);
        await command.ExecuteNonQueryAsync();
    }
}
