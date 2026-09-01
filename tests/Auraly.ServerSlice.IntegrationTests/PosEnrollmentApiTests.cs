using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosEnrollmentApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Authorized_user_enrolls_a_device_and_code_can_only_be_redeemed_once()
    {
        await PrepareInitialCashierAsync();
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.EnrolledDevicesEnroll,
            CommercePermissionCodes.SalesCreate);

        using var authorizationResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId,
                fixture.WarehouseId,
                "Equipo recepción"));
        authorizationResponse.EnsureSuccessStatusCode();
        var authorization =
            await authorizationResponse.Content.ReadFromJsonAsync<PosEnrollmentAuthorization>();
        Assert.NotNull(authorization);
        Assert.Equal(fixture.BusinessId, authorization.Workspace.BusinessId);
        Assert.Equal(fixture.WarehouseId, authorization.Workspace.WarehouseId);
        Assert.True(authorization.ExpiresAt > DateTimeOffset.UtcNow);

        var redeemRequest = new RedeemPosEnrollmentRequest(
            authorization.EnrollmentSessionId,
            authorization.RedemptionCode,
            "WORKSTATION-01");
        using var redeemResponse = await client.PostAsJsonAsync(
            "/api/pos/v1/enrollments/redeem",
            redeemRequest);
        Assert.True(redeemResponse.IsSuccessStatusCode,
            await redeemResponse.Content.ReadAsStringAsync());
        var package =
            await redeemResponse.Content.ReadFromJsonAsync<PosEnrollmentPackage>();
        Assert.NotNull(package);
        Assert.NotEqual(Guid.Empty, package.DeviceId);
        Assert.Equal(fixture.BusinessId, package.BusinessId);
        Assert.False(string.IsNullOrWhiteSpace(package.CompanyName));
        Assert.Equal(fixture.WarehouseId, package.WarehouseId);
        Assert.Matches("^(?!00)\\d{2}$", package.DocumentSeries.SeriesCode);
        Assert.NotEmpty(package.DeviceSecret);
        Assert.NotNull(package.OfflineLeaseTrustedPublicKeys);
        Assert.Equal(
            fixture.OfflineLeasePublicKeyPem,
            package.OfflineLeaseTrustedPublicKeys![ServerSliceFixture.OfflineLeaseKeyId]);
        Assert.Null(package.FiscalSeries);
        Assert.NotEqual(Guid.Empty, package.DocumentSeries.SeriesId);
        Assert.Contains("catalog.sync", package.Permissions);
        Assert.Contains(CommercePermissionCodes.SalesCreate, package.Permissions);
        Assert.NotNull(package.InitialOfflineAccess);
        Assert.Equal(package.InitialUserId, package.InitialOfflineAccess!.User.UserId);
        Assert.Contains(
            CommercePermissionCodes.SalesCreate,
            package.InitialOfflineAccess.User.Permissions);

        using var repeated = await client.PostAsJsonAsync(
            "/api/pos/v1/enrollments/redeem",
            redeemRequest);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*),MAX(CASE WHEN CredentialHash IS NOT NULL THEN 1 ELSE 0 END)
            FROM dbo.EnrolledDevices
            WHERE DeviceId=@DeviceId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@DeviceId", package.DeviceId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    [Fact]
    public async Task Reenrollment_recovers_device_identity_when_local_package_was_lost()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.EnrolledDevicesEnroll);

        using var firstAuthorizationResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId, fixture.WarehouseId, "Equipo reconfigurable"));
        firstAuthorizationResponse.EnsureSuccessStatusCode();
        var firstAuthorization = await firstAuthorizationResponse.Content
            .ReadFromJsonAsync<PosEnrollmentAuthorization>();
        Assert.NotNull(firstAuthorization);
        using var firstRedeemResponse = await client.PostAsJsonAsync(
            "/api/pos/v1/enrollments/redeem",
            new RedeemPosEnrollmentRequest(
                firstAuthorization.EnrollmentSessionId,
                firstAuthorization.RedemptionCode,
                "WORKSTATION-REENROLL"));
        firstRedeemResponse.EnsureSuccessStatusCode();
        var firstPackage = await firstRedeemResponse.Content
            .ReadFromJsonAsync<PosEnrollmentPackage>();
        Assert.NotNull(firstPackage);

        using var secondAuthorizationResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId, fixture.WarehouseId, "Equipo reconfigurable"));
        secondAuthorizationResponse.EnsureSuccessStatusCode();
        var secondAuthorization = await secondAuthorizationResponse.Content
            .ReadFromJsonAsync<PosEnrollmentAuthorization>();
        Assert.NotNull(secondAuthorization);
        using var secondRedeemResponse = await client.PostAsJsonAsync(
            "/api/pos/v1/enrollments/redeem",
            new RedeemPosEnrollmentRequest(
                secondAuthorization.EnrollmentSessionId,
                secondAuthorization.RedemptionCode,
                "WORKSTATION-REENROLL"));
        secondRedeemResponse.EnsureSuccessStatusCode();
        var secondPackage = await secondRedeemResponse.Content
            .ReadFromJsonAsync<PosEnrollmentPackage>();
        Assert.NotNull(secondPackage);
        Assert.Equal(firstPackage.DeviceId, secondPackage.DeviceId);
        Assert.Equal(firstPackage.DocumentSeries.SeriesId, secondPackage.DocumentSeries.SeriesId);
        Assert.Equal(firstPackage.DocumentSeries.SeriesCode, secondPackage.DocumentSeries.SeriesCode);
        Assert.NotEqual(firstPackage.DeviceSecret, secondPackage.DeviceSecret);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.DocumentSeries
               WHERE DeviceId=@DeviceId AND DocumentType=N'SalesInvoice'),
              (SELECT COUNT(*) FROM dbo.DocumentSeriesCursors
               WHERE DocumentSeriesId=@SeriesId),
              (SELECT COUNT(*) FROM dbo.EnrolledDevices
               WHERE DeviceId=@DeviceId AND IsActive=1);
            """, connection);
        command.Parameters.AddWithValue("@DeviceId", firstPackage.DeviceId);
        command.Parameters.AddWithValue("@SeriesId", firstPackage.DocumentSeries.SeriesId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public async Task Unenrolled_workstation_can_enroll_again_with_a_new_active_identity()
    {
        const string installationId = "WORKSTATION-AFTER-UNENROLL";
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.EnrolledDevicesEnroll);

        var first = await EnrollAsync(client, installationId);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                UPDATE dbo.EnrolledDevices SET IsActive=0 WHERE DeviceId=@DeviceId;
                UPDATE dbo.DocumentSeries SET IsActive=0 WHERE DeviceId=@DeviceId;
                """, connection);
            command.Parameters.AddWithValue("@DeviceId", first.DeviceId);
            await command.ExecuteNonQueryAsync();
        }

        var second = await EnrollAsync(client, installationId);

        Assert.NotEqual(first.DeviceId, second.DeviceId);
        Assert.NotEqual(first.DeviceSecret, second.DeviceSecret);
        using var oldSync = DeviceRequest(
            $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId:D}",
            first);
        using var oldSyncResponse = await client.SendAsync(oldSync);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSyncResponse.StatusCode);
        using var newSync = DeviceRequest(
            $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId:D}",
            second);
        using var newSyncResponse = await client.SendAsync(newSync);
        Assert.Equal(HttpStatusCode.OK, newSyncResponse.StatusCode);
        await using var verification = new SqlConnection(fixture.ConnectionString);
        await verification.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.EnrolledDevices
               WHERE DeviceId=@OldDeviceId AND IsActive=0),
              (SELECT COUNT(*) FROM dbo.EnrolledDevices
               WHERE DeviceId=@NewDeviceId AND IsActive=1);
            """, verification);
        verify.Parameters.AddWithValue("@OldDeviceId", first.DeviceId);
        verify.Parameters.AddWithValue("@NewDeviceId", second.DeviceId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }
    [Fact]
    public async Task New_device_enrollment_never_exceeds_tenant_capacity()
    {
        var original = await ReadDeviceCapacityAsync();
        await SetDeviceCapacityAsync(original.ActiveDevices + 1);
        try
        {
            using var client = fixture.CreateAdminClient(
                CommercePermissionCodes.EnrolledDevicesEnroll);
            using var authorizationResponse = await client.PostAsJsonAsync(
                "/api/commerce/v1/pos/enrollments",
                new CreatePosEnrollmentRequest(
                    fixture.BusinessId,
                    fixture.WarehouseId,
                    "Equipo sobre cupo"));
            authorizationResponse.EnsureSuccessStatusCode();
            var authorization = await authorizationResponse.Content
                .ReadFromJsonAsync<PosEnrollmentAuthorization>();
            Assert.NotNull(authorization);

            // Another workstation can consume the last slot between preflight and redeem.
            // Redemption must keep the transactional capacity check as the final authority.
            await SetDeviceCapacityAsync(original.ActiveDevices);

            using var redeemResponse = await client.PostAsJsonAsync(
                "/api/pos/v1/enrollments/redeem",
                new RedeemPosEnrollmentRequest(
                    authorization.EnrollmentSessionId,
                    authorization.RedemptionCode,
                    "WORKSTATION-OVER-CAPACITY"));

            Assert.Equal(HttpStatusCode.Conflict, redeemResponse.StatusCode);
            Assert.Equal(original.ActiveDevices, (await ReadDeviceCapacityAsync()).ActiveDevices);
        }
        finally
        {
            await SetDeviceCapacityAsync(original.MaximumDevices);
        }
    }
    [Fact]
    public async Task User_without_enrollment_permission_must_present_supervisor_approval()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId,
                fixture.WarehouseId,
                "Equipo no autorizado"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "identificador de operación",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DeviceCapacityState> ReadDeviceCapacityAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT t.MaximumEnrolledDevices,
                   (SELECT COUNT(*) FROM dbo.EnrolledDevices d
                    WHERE d.TenantId=t.TenantId AND d.IsActive=1)
            FROM dbo.Tenants t
            WHERE t.TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DeviceCapacityState(reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task SetDeviceCapacityAsync(int maximumDevices)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.Tenants SET MaximumEnrolledDevices=@MaximumDevices WHERE TenantId=@TenantId;",
            connection);
        command.Parameters.AddWithValue("@MaximumDevices", maximumDevices);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
    private async Task PrepareInitialCashierAsync()
    {
        var verifier = PosOfflinePasswordHasher.Hash(
            "Integration-Cashier-Password-1",
            DateTimeOffset.UtcNow);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            IF NOT EXISTS(
                SELECT 1
                FROM dbo.RolePermissions rolePermission
                JOIN dbo.Permissions permission
                  ON permission.PermissionId=rolePermission.PermissionId
                WHERE rolePermission.RoleId=@RoleId AND permission.Resource=@SalesCreate)
              INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
              SELECT NEWID(),@RoleId,PermissionId,SYSDATETIMEOFFSET()
              FROM dbo.Permissions WHERE Resource=@SalesCreate;
            UPDATE dbo.AppUsers
            SET PosOfflinePasswordSalt=@Salt,
                PosOfflinePasswordHash=@Hash,
                PosOfflinePasswordIterations=@Iterations,
                PosOfflinePasswordChangedAt=@ChangedAt
            WHERE UserId=@UserId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@RoleId", fixture.RoleId);
        command.Parameters.AddWithValue("@SalesCreate", CommercePermissionCodes.SalesCreate);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.Add("@Salt", System.Data.SqlDbType.VarBinary, 16).Value = verifier.Salt;
        command.Parameters.Add("@Hash", System.Data.SqlDbType.VarBinary, 32).Value = verifier.Hash;
        command.Parameters.AddWithValue("@Iterations", verifier.Iterations);
        command.Parameters.AddWithValue("@ChangedAt", verifier.ChangedAt);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<PosEnrollmentPackage> EnrollAsync(
        HttpClient client,
        string installationId)
    {
        using var authorizationResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId, fixture.WarehouseId, "Equipo re-enrollable"));
        authorizationResponse.EnsureSuccessStatusCode();
        var authorization = await authorizationResponse.Content
            .ReadFromJsonAsync<PosEnrollmentAuthorization>();
        Assert.NotNull(authorization);
        using var redeemResponse = await client.PostAsJsonAsync(
            "/api/pos/v1/enrollments/redeem",
            new RedeemPosEnrollmentRequest(
                authorization.EnrollmentSessionId,
                authorization.RedemptionCode,
                installationId));
        redeemResponse.EnsureSuccessStatusCode();
        return (await redeemResponse.Content
            .ReadFromJsonAsync<PosEnrollmentPackage>())!;
    }

    private static HttpRequestMessage DeviceRequest(
        string path,
        PosEnrollmentPackage enrollment)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(
            "X-Auraly-Device-Id", enrollment.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", enrollment.DeviceSecret);
        return request;
    }

    private sealed record DeviceCapacityState(int MaximumDevices, int ActiveDevices);
}
