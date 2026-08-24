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
        var fiscalSeriesId = Guid.NewGuid();
        await SeedAvailableDeviceFiscalSeriesAsync(fiscalSeriesId);
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.EnrolledDevicesEnroll);

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
        redeemResponse.EnsureSuccessStatusCode();
        var package =
            await redeemResponse.Content.ReadFromJsonAsync<PosEnrollmentPackage>();
        Assert.NotNull(package);
        Assert.NotEqual(Guid.Empty, package.DeviceId);
        Assert.Equal(fixture.BusinessId, package.BusinessId);
        Assert.Equal(fixture.WarehouseId, package.WarehouseId);
        Assert.Matches("^(?!00)\\d{2}$", package.DocumentSeries.SeriesCode);
        Assert.NotEmpty(package.DeviceSecret);
        Assert.NotNull(package.OfflineLeaseTrustedPublicKeys);
        Assert.Equal(
            fixture.OfflineLeasePublicKeyPem,
            package.OfflineLeaseTrustedPublicKeys![ServerSliceFixture.OfflineLeaseKeyId]);
        var fiscal = Assert.IsType<PosEnrollmentFiscalSeries>(package.FiscalSeries);
        Assert.Equal(ServerSliceFixture.TechnicalKeyValue, fiscal.TechnicalKey);
        Assert.NotEqual(Guid.Empty, package.DocumentSeries.SeriesId);
        Assert.Contains("catalog.sync", package.Permissions);
        Assert.Contains(CommercePermissionCodes.SalesCreate, package.Permissions);

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
    public async Task Reenrollment_preserves_device_series_and_local_numbering_identity()
    {
        var fiscalSeriesId = Guid.NewGuid();
        await SeedAvailableDeviceFiscalSeriesAsync(fiscalSeriesId);
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
                "WORKSTATION-REENROLL",
                firstPackage.DeviceId));
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
    public async Task New_device_enrollment_never_exceeds_tenant_capacity()
    {
        var original = await ReadDeviceCapacityAsync();
        await SetDeviceCapacityAsync(original.ActiveDevices);
        var fiscalSeriesId = Guid.NewGuid();
        await SeedAvailableDeviceFiscalSeriesAsync(fiscalSeriesId);

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
    private async Task SeedAvailableDeviceFiscalSeriesAsync(Guid fiscalSeriesId)
    {
        const string sql = """
            INSERT dbo.FiscalSeries
              (SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
               DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES
              (@FiscalSeriesId,@BusinessId,NULL,N'Device',@FiscalAuthorizationId,
               N'SalesInvoice',@Prefix,20001,30000,1,SYSDATETIMEOFFSET());
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@FiscalSeriesId", fiscalSeriesId);
        command.Parameters.AddWithValue(
            "@FiscalAuthorizationId", fixture.FiscalAuthorizationId);
        command.Parameters.AddWithValue(
            "@Prefix",
            ("T" + fiscalSeriesId.ToString("N")[..3]).ToUpperInvariant());
        await command.ExecuteNonQueryAsync();
    }
    private sealed record DeviceCapacityState(int MaximumDevices, int ActiveDevices);
}
