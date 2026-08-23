using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class FiscalDeviceSeriesApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Administrator_assigns_a_range_to_an_enrolled_device_and_read_only_user_cannot_mutate_it()
    {
        var documentSeriesId = Guid.NewGuid();
        var poolId = Guid.NewGuid();
        var expiredAuthorizationId = Guid.NewGuid();
        var expiredPoolId = Guid.NewGuid();
        await SeedDeviceScopeAndPoolAsync(
            documentSeriesId, poolId, expiredAuthorizationId, expiredPoolId);

        try
        {
            using var readOnly = fixture.CreateAdminClient(
                FiscalPermissionCodes.ConfigurationRead);
            using var beforeResponse = await readOnly.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/devices?businessId={fixture.BusinessId:D}");
            Assert.True(beforeResponse.IsSuccessStatusCode,
                await beforeResponse.Content.ReadAsStringAsync());
            var before = await beforeResponse.Content
                .ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>();
            Assert.NotNull(before);
            Assert.Equal(100, before.AvailableConsecutives);
            Assert.Contains(before.Devices, device =>
                device.DeviceId == fixture.DeniedDeviceId && !device.IsProvisioned);

            using var deniedResponse = await readOnly.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, 25));
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

            using var manager = fixture.CreateAdminClient(
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            using var assignedResponse = await manager.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, 25));
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
            var assigned = await assignedResponse.Content
                .ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>();
            Assert.NotNull(assigned);
            Assert.Equal(75, assigned.AvailableConsecutives);
            var deviceAssignment = Assert.Single(assigned.Devices, device =>
                device.DeviceId == fixture.DeniedDeviceId);
            Assert.True(deviceAssignment.IsProvisioned);
            Assert.Equal(30001, deviceAssignment.RangeStart);
            Assert.Equal(30025, deviceAssignment.RangeEnd);

            using var duplicateResponse = await manager.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId, 1));
            Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning' AND AvailableThroughCursor=30001"));
        }
        finally
        {
            await ExecuteAsync("""
                DELETE dbo.PosSynchronizationOutboxMessages
                WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning'
                  AND AvailableThroughCursor=30001;
                DELETE dbo.FiscalSeries
                WHERE BusinessId=@BusinessId AND EmitterKind=N'Device'
                  AND (DeviceId=@DeviceId OR SeriesId IN(@PoolId,@ExpiredPoolId));
                DELETE dbo.DocumentSeries WHERE DocumentSeriesId=@DocumentSeriesId;
                DELETE dbo.FiscalAuthorizations
                WHERE FiscalAuthorizationId=@ExpiredAuthorizationId;
                """, documentSeriesId, poolId, expiredAuthorizationId, expiredPoolId);
        }
    }

    private async Task SeedDeviceScopeAndPoolAsync(
        Guid documentSeriesId, Guid poolId, Guid expiredAuthorizationId,
        Guid expiredPoolId) =>
        await ExecuteAsync("""
            INSERT dbo.DocumentSeries
              (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
               Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES
              (@DocumentSeriesId,@BusinessId,@DeviceId,N'SalesInvoice',N'VTA',N'98',
               8,1,99999999,1,1,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalSeries
              (SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
               DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES
              (@PoolId,@BusinessId,NULL,N'Device',@FiscalAuthorizationId,
               N'SalesInvoice',N'FV99',30001,30100,1,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalAuthorizations(
              FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
              Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,
              AuthorizedRangeStart,AuthorizedRangeEnd,IsActive,CreatedAt)
            VALUES(
              @ExpiredAuthorizationId,@BusinessId,N'EXPIRED-TEST',N'9001234567',2,
              N'https://example.test/qr',N'expired-test','2020-01-01','2020-12-31',
              40001,40020,1,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalSeries(
              SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
              DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(
              @ExpiredPoolId,@BusinessId,NULL,N'Device',@ExpiredAuthorizationId,
              N'SalesInvoice',N'OLD',40001,40020,1,SYSDATETIMEOFFSET());
            """, documentSeriesId, poolId, expiredAuthorizationId, expiredPoolId);

    private async Task ExecuteAsync(
        string sql, Guid documentSeriesId, Guid poolId,
        Guid expiredAuthorizationId, Guid expiredPoolId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@DeviceId", fixture.DeniedDeviceId);
        command.Parameters.AddWithValue("@FiscalAuthorizationId", fixture.FiscalAuthorizationId);
        command.Parameters.AddWithValue("@DocumentSeriesId", documentSeriesId);
        command.Parameters.AddWithValue("@PoolId", poolId);
        command.Parameters.AddWithValue("@ExpiredAuthorizationId", expiredAuthorizationId);
        command.Parameters.AddWithValue("@ExpiredPoolId", expiredPoolId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
