using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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

        using var deviceStatusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/approvals/{created.ApprovalRequestId:D}");
        deviceStatusRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        deviceStatusRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        deviceStatusRequest.Headers.Add("X-Auraly-User-Id", fixture.UserId.ToString("D"));
        deviceStatusRequest.Headers.Add("X-Auraly-Work-Session-Id", fixture.WorkSessionId.ToString("D"));
        using var deviceStatusResponse = await deviceCreationClient.SendAsync(deviceStatusRequest);
        deviceStatusResponse.EnsureSuccessStatusCode();
        var deviceStatus = await deviceStatusResponse.Content.ReadFromJsonAsync<PosApprovalRequestView>();
        Assert.Equal(PosApprovalStatus.Pending, deviceStatus!.Status);

        using var foreignDeviceStatusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/approvals/{created.ApprovalRequestId:D}");
        foreignDeviceStatusRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        foreignDeviceStatusRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        foreignDeviceStatusRequest.Headers.Add("X-Auraly-User-Id", Guid.NewGuid().ToString("D"));
        foreignDeviceStatusRequest.Headers.Add("X-Auraly-Work-Session-Id", fixture.WorkSessionId.ToString("D"));
        using var foreignDeviceStatusResponse = await deviceCreationClient.SendAsync(foreignDeviceStatusRequest);
        Assert.Equal(HttpStatusCode.Forbidden, foreignDeviceStatusResponse.StatusCode);

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

    [Theory]
    [InlineData(CommercePermissionCodes.SalesRemoveLine, "RemoveLine")]
    [InlineData(CommercePermissionCodes.SalesRestartDraft, "RestartSale")]
    [InlineData(CommercePermissionCodes.SalesDiscount, "Discount")]
    [InlineData("work-sessions.close", "CloseWorkSession")]
    public async Task Every_sensitive_pos_action_accepts_local_window_and_remote_approval(
        string permissionResource,
        string action)
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        using var requester = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            permissionResource,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.PosApprovalsAuthorize,
            CommercePermissionCodes.PosApprovalsManageCredential);
        var secret = $"Secondary-{Guid.NewGuid():N}"[..24];
        using var configured = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest(secret, 8));
        configured.EnsureSuccessStatusCode();

        async Task<PosApprovalRequestView> CreateAsync() =>
            (await (await requester.PostAsJsonAsync(
                "/api/commerce/v1/pos/approvals/",
                new CreatePosApprovalRequest(
                    fixture.BusinessId,
                    fixture.DeviceId,
                    fixture.WorkSessionId,
                    Guid.NewGuid(),
                    permissionResource == CommercePermissionCodes.SalesRemoveLine
                        ? Guid.NewGuid()
                        : null,
                    permissionResource,
                    $"{{\"action\":\"{action}\"}}")))
                .Content.ReadFromJsonAsync<PosApprovalRequestView>())!;

        var local = await CreateAsync();
        using var localResponse = await requester.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{local.ApprovalRequestId:D}/local-authorization",
            new AuthorizePosApprovalLocallyRequest(secret));
        localResponse.EnsureSuccessStatusCode();

        var remote = await CreateAsync();
        using var remoteResponse = await supervisor.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{remote.ApprovalRequestId:D}/decision",
            new DecidePosApprovalRequest(true));
        remoteResponse.EnsureSuccessStatusCode();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ApprovalRequestId,Status,DecisionMethod,DecidedByUserId
            FROM dbo.PosApprovalRequests
            WHERE ApprovalRequestId IN(@LocalId,@RemoteId);
            """;
        command.Parameters.AddWithValue("@LocalId", local.ApprovalRequestId);
        command.Parameters.AddWithValue("@RemoteId", remote.ApprovalRequestId);
        await using var reader = await command.ExecuteReaderAsync();
        var decisions = new Dictionary<Guid, (string Status, string Method, Guid UserId)>();
        while (await reader.ReadAsync())
            decisions[reader.GetGuid(0)] = (
                reader.GetString(1), reader.GetString(2), reader.GetGuid(3));
        Assert.Equal((PosApprovalStatus.Approved, "LocalSecret", supervisorId),
            decisions[local.ApprovalRequestId]);
        Assert.Equal((PosApprovalStatus.Approved, "Remote", supervisorId),
            decisions[remote.ApprovalRequestId]);
    }

    [Fact]
    public async Task Push_subscription_requires_receive_permission_and_pending_keeps_each_register_separate()
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        var subscription = new
        {
            endpoint = $"https://push.test.local/{Guid.NewGuid():N}",
            p256dh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(65)),
            auth = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
        };
        using var withoutReceive = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.PosApprovalsAuthorize);
        using var forbidden = await withoutReceive.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/push/subscription", subscription);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.PosApprovalsRead,
            CommercePermissionCodes.PosApprovalsAuthorize,
            CommercePermissionCodes.PosApprovalsReceiveNotifications);
        using var subscribed = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/push/subscription", subscription);
        Assert.Equal(HttpStatusCode.NoContent, subscribed.StatusCode);

        using var requester = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        var first = await (await requester.PostAsJsonAsync(
            "/api/commerce/v1/pos/approvals/",
            new CreatePosApprovalRequest(fixture.BusinessId, fixture.DeviceId, fixture.WorkSessionId,
                Guid.NewGuid(), null, CommercePermissionCodes.SalesRestartDraft, "{\"action\":\"RestartSale\",\"register\":\"Caja A\"}")))
            .Content.ReadFromJsonAsync<PosApprovalRequestView>();
        var second = await (await requester.PostAsJsonAsync(
            "/api/commerce/v1/pos/approvals/",
            new CreatePosApprovalRequest(fixture.BusinessId, fixture.DeniedDeviceId, fixture.WorkSessionId,
                Guid.NewGuid(), null, CommercePermissionCodes.SalesDiscount, "{\"action\":\"Discount\",\"register\":\"Caja B\"}")))
            .Content.ReadFromJsonAsync<PosApprovalRequestView>();

        var pending = await supervisor.GetFromJsonAsync<List<PosApprovalRequestView>>(
            "/api/commerce/v1/pos/approvals/pending");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Contains(pending!, item => item.ApprovalRequestId == first.ApprovalRequestId);
        Assert.Contains(pending!, item => item.ApprovalRequestId == second.ApprovalRequestId);
        Assert.NotEqual(first.ApprovalRequestId, second.ApprovalRequestId);
    }

    [Fact]
    public async Task Secondary_credential_can_be_reset_for_eight_hours_one_week_or_always_and_revoked()
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.PosApprovalsManageCredential);

        var started = DateTimeOffset.UtcNow;
        using var weekly = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest("Weekly-Secondary-1", 168));
        weekly.EnsureSuccessStatusCode();
        var weeklyStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.True(weeklyStatus!.IsConfigured);
        Assert.InRange(weeklyStatus.ValidUntil!.Value,
            started.AddDays(6).AddHours(23), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        using var reset = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest("Eight-Hour-Secondary-2", 8));
        reset.EnsureSuccessStatusCode();
        var resetStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.InRange(resetStatus!.ValidUntil!.Value,
            started.AddHours(7).AddMinutes(59), DateTimeOffset.UtcNow.AddHours(8).AddMinutes(1));

        using var permanent = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest("Permanent-Secondary-3", null));
        permanent.EnsureSuccessStatusCode();
        var permanentStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.True(permanentStatus!.IsConfigured);
        Assert.Null(permanentStatus.ValidUntil);

        using var revoked = await supervisor.DeleteAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var revokedStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.False(revokedStatus!.IsConfigured);
        Assert.Null(revokedStatus.ValidUntil);
    }

    [Fact]
    public async Task One_time_secondary_credential_is_consumed_by_its_first_authorization()
    {
        var supervisorId = Guid.NewGuid();
        await SeedSupervisorAsync(supervisorId);
        using var supervisor = fixture.CreateUserClient(
            supervisorId,
            CommercePermissionCodes.PosApprovalsManageCredential);
        using var configured = await supervisor.PutAsJsonAsync(
            "/api/commerce/v1/pos/approvals/supervisor-credential",
            new ConfigureSupervisorCredentialRequest(
                "One-Time-Secondary-1", null, true));
        configured.EnsureSuccessStatusCode();
        var configuredStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.True(configuredStatus!.IsConfigured);
        Assert.True(configuredStatus.IsOneTime);

        using var requester = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        async Task<PosApprovalRequestView> CreateRequestAsync()
        {
            using var response = await requester.PostAsJsonAsync(
                "/api/commerce/v1/pos/approvals/",
                new CreatePosApprovalRequest(
                    fixture.BusinessId, null, null, Guid.NewGuid(), null,
                    CommercePermissionCodes.SalesDiscount,
                    "{\"action\":\"Discount\"}"));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PosApprovalRequestView>())!;
        }

        var first = await CreateRequestAsync();
        using var firstAuthorization = await requester.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{first.ApprovalRequestId:D}/local-authorization",
            new AuthorizePosApprovalLocallyRequest("One-Time-Secondary-1"));
        firstAuthorization.EnsureSuccessStatusCode();

        var consumedStatus = await supervisor.GetFromJsonAsync<SupervisorCredentialStatusView>(
            "/api/commerce/v1/pos/approvals/supervisor-credential");
        Assert.False(consumedStatus!.IsConfigured);

        var second = await CreateRequestAsync();
        using var secondAuthorization = await requester.PostAsJsonAsync(
            $"/api/commerce/v1/pos/approvals/{second.ApprovalRequestId:D}/local-authorization",
            new AuthorizePosApprovalLocallyRequest("One-Time-Secondary-1"));
        Assert.Equal(HttpStatusCode.BadRequest, secondAuthorization.StatusCode);
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
              N'work-sessions.close',
              N'pos.approvals.read',N'pos.approvals.authorize',N'pos.approvals.receive_notifications',N'pos.approvals.manage_credential');
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
