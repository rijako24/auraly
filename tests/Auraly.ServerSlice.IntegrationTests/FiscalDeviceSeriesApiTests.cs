using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class FiscalDeviceSeriesApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Manager_assigns_complete_resolution_idempotently_and_read_only_cannot_mutate()
    {
        var rangeId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        await SeedAsync(rangeId, fixture.DeniedDeviceId, seriesId, "DI98", 81001, 81100);
        try
        {
            using var readOnly = fixture.CreateAdminClient(FiscalPermissionCodes.ConfigurationRead);
            var before = await WorkspaceAsync(readOnly);
            Assert.Contains(before.AvailableResolutions, x => x.DianNumberingRangeId == rangeId);
            Assert.Equal(100, before.AvailableConsecutives);
            using var denied = await readOnly.PostAsJsonAsync(AssignmentUrl,
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, rangeId));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            using var manager = fixture.CreateAdminClient(
                FiscalPermissionCodes.ConfigurationRead, FiscalPermissionCodes.ConfigurationManage);
            using var response = await manager.PostAsJsonAsync(AssignmentUrl,
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, rangeId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var assigned = (await response.Content.ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>())!;
            Assert.DoesNotContain(assigned.AvailableResolutions, x => x.DianNumberingRangeId == rangeId);
            var device = Assert.Single(assigned.Devices, x => x.DeviceId == fixture.DeniedDeviceId);
            Assert.True(device.IsProvisioned);
            Assert.Equal("DI98", device.Prefix);
            Assert.Equal(81001, device.RangeStart);
            Assert.Equal(81100, device.RangeEnd);

            using var repeated = await manager.PostAsJsonAsync(AssignmentUrl,
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, rangeId));
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            Assert.Equal(1, await ScalarAsync("""
                SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
                WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
                """));
            Assert.Equal(1, await ScalarAsync("""
                SELECT COUNT(*) FROM dbo.FiscalSeries s
                JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
                WHERE s.DeviceId=@DeviceId AND a.DianNumberingRangeId=@RangeId
                  AND s.RangeStart=81001 AND s.RangeEnd=81100 AND s.IsActive=1;
                """, fixture.DeniedDeviceId, rangeId));
        }
        finally { await CleanupAsync(rangeId, [seriesId], [fixture.DeniedDeviceId], false); }
    }

    [Fact]
    public async Task One_resolution_cannot_be_assigned_concurrently_to_two_devices()
    {
        var rangeId = Guid.NewGuid();
        var devices = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var series = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await SeedAsync(rangeId, devices[0], series[0], "DIC", 82001, 82100, true);
        await SeedDeviceAsync(devices[1], series[1], true);
        try
        {
            using var first = fixture.CreateAdminClient(FiscalPermissionCodes.ConfigurationRead, FiscalPermissionCodes.ConfigurationManage);
            using var second = fixture.CreateAdminClient(FiscalPermissionCodes.ConfigurationRead, FiscalPermissionCodes.ConfigurationManage);
            var responses = await Task.WhenAll(
                first.PostAsJsonAsync(AssignmentUrl, new AssignFiscalDeviceSeriesRequest(devices[0], rangeId)),
                second.PostAsJsonAsync(AssignmentUrl, new AssignFiscalDeviceSeriesRequest(devices[1], rangeId)));
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(1, await ScalarAsync("""
                SELECT COUNT(*) FROM dbo.FiscalSeries s
                JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
                WHERE a.DianNumberingRangeId=@RangeId AND s.IsActive=1;
                """, rangeId: rangeId));
        }
        finally { await CleanupAsync(rangeId, series, devices, true); }
    }

    private string AssignmentUrl =>
        $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}";

    private async Task<FiscalDeviceSeriesWorkspace> WorkspaceAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            $"/api/commerce/v1/fiscal/configuration/devices?businessId={fixture.BusinessId:D}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>())!;
    }

    private async Task SeedAsync(Guid rangeId, Guid deviceId, Guid documentSeriesId,
        string prefix, long start, long end, bool createDevice = false)
    {
        await ExecuteAsync("""
            UPDATE dbo.FiscalIssuerConfigurations SET Environment=1
            WHERE BusinessId=@BusinessId AND IsActive=1;
            """, environment: 1);
        await SeedDeviceAsync(deviceId, documentSeriesId, createDevice);
        await ExecuteAsync("""
            INSERT fiscal.DianNumberingRanges(
                DianNumberingRangeId,TenantId,AuthorizationNumber,ResolutionDate,Prefix,
                RangeStart,RangeEnd,ValidFrom,ValidUntil,ProtectedTechnicalKey,ImportedAt,LastSeenAt)
            SELECT @RangeId,@TenantId,CONCAT(N'TEST-',CONVERT(nvarchar(36),@RangeId)),CONVERT(date,SYSUTCDATETIME()),
                   @Prefix,@Start,@End,DATEADD(day,-1,CONVERT(date,SYSUTCDATETIME())),
                   DATEADD(year,1,CONVERT(date,SYSUTCDATETIME())),@ProtectedTechnicalKey,
                   SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET();
            """, deviceId, rangeId, prefix, start, end);
    }

    private Task SeedDeviceAsync(Guid deviceId, Guid documentSeriesId, bool createDevice) => ExecuteAsync("""
        IF @CreateDevice=1
            INSERT dbo.EnrolledDevices(DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                CredentialIterations,IsActive,CreatedAt)
            VALUES(@DeviceId,@TenantId,N'POS DIAN test',0x01,0x02,100000,1,SYSDATETIMEOFFSET());
        INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,
            Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
        VALUES(@DocumentSeriesId,@BusinessId,@DeviceId,N'SalesInvoice',N'VTA',
            RIGHT(CONCAT(N'00',ABS(CHECKSUM(@DocumentSeriesId))%100),2),8,1,99999999,1,1,SYSDATETIMEOFFSET());
        """, deviceId, documentSeriesId: documentSeriesId, createDevice: createDevice);

    private async Task CleanupAsync(Guid rangeId, IReadOnlyCollection<Guid> documentSeriesIds,
        IReadOnlyCollection<Guid> deviceIds, bool deleteDevices)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var deviceId in deviceIds)
        {
            await using var command = new SqlCommand("""
                DELETE dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
                DELETE secret FROM dbo.FiscalTechnicalKeySecrets secret JOIN dbo.FiscalAuthorizations a
                  ON a.FiscalAuthorizationId=secret.FiscalAuthorizationId WHERE a.DianNumberingRangeId=@RangeId;
                DELETE series FROM dbo.FiscalSeries series JOIN dbo.FiscalAuthorizations a
                  ON a.FiscalAuthorizationId=series.FiscalAuthorizationId WHERE a.DianNumberingRangeId=@RangeId;
                DELETE dbo.FiscalAuthorizations WHERE DianNumberingRangeId=@RangeId;
                DELETE dbo.DocumentSeries WHERE DeviceId=@DeviceId AND DocumentSeriesId<>@FixtureDocumentSeriesId;
                IF @DeleteDevice=1 DELETE dbo.EnrolledDevices WHERE DeviceId=@DeviceId;
                """, connection);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@RangeId", rangeId);
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            command.Parameters.AddWithValue("@FixtureDocumentSeriesId", Guid.Empty);
            command.Parameters.AddWithValue("@DeleteDevice", deleteDevices);
            await command.ExecuteNonQueryAsync();
        }
        await using var finish = new SqlCommand("""
            DELETE fiscal.DianNumberingRanges WHERE DianNumberingRangeId=@RangeId;
            UPDATE dbo.FiscalIssuerConfigurations SET Environment=2 WHERE BusinessId=@BusinessId AND IsActive=1;
            """, connection);
        finish.Parameters.AddWithValue("@RangeId", rangeId);
        finish.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await finish.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(string sql, Guid? deviceId = null, Guid? rangeId = null,
        string? prefix = null, long? start = null, long? end = null,
        Guid? documentSeriesId = null, bool createDevice = false, int? environment = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@FixtureAuthorizationId", fixture.FiscalAuthorizationId);
        command.Parameters.AddWithValue("@DeviceId", deviceId ?? Guid.Empty);
        command.Parameters.AddWithValue("@RangeId", rangeId ?? Guid.Empty);
        command.Parameters.AddWithValue("@Prefix", prefix ?? string.Empty);
        command.Parameters.AddWithValue("@Start", start ?? 0);
        command.Parameters.AddWithValue("@End", end ?? 0);
        command.Parameters.AddWithValue("@DocumentSeriesId", documentSeriesId ?? Guid.Empty);
        command.Parameters.AddWithValue("@CreateDevice", createDevice);
        command.Parameters.AddWithValue("@Environment", environment ?? 0);
        command.Parameters.AddWithValue("@ProtectedTechnicalKey", ProtectTechnicalKey());
        await command.ExecuteNonQueryAsync();
    }

    private static byte[] ProtectTechnicalKey()
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(ServerSliceFixture.TechnicalKeyValue);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(ServerSliceFixture.FiscalSecretProtectionKey, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    private async Task<int> ScalarAsync(string sql, Guid? deviceId = null, Guid? rangeId = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@DeviceId", deviceId ?? Guid.Empty);
        command.Parameters.AddWithValue("@RangeId", rangeId ?? Guid.Empty);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
