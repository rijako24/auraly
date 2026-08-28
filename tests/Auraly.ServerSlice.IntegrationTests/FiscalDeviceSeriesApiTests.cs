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
    public async Task Concurrent_devices_receive_disjoint_active_and_standby_blocks()
    {
        var devices = Enumerable.Range(1, 3).Select(_ => Guid.NewGuid()).ToArray();
        var poolId = Guid.NewGuid();
        await using var seed = new SqlConnection(fixture.ConnectionString);
        await seed.OpenAsync();
        await using (var command = new SqlCommand("""
            INSERT fiscal.FiscalNumberingPolicies(BusinessId,DocumentType,BlockSize,UpdatedAt)
            VALUES(@BusinessId,N'SalesInvoice',10,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,AllocationState,IsActive,CreatedAt)
            VALUES(@PoolId,@BusinessId,NULL,N'Device',@FiscalAuthorizationId,
                N'SalesInvoice',N'FC',60001,60100,N'Pool',1,SYSDATETIMEOFFSET());
            """, seed))
        {
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@PoolId", poolId);
            command.Parameters.AddWithValue("@FiscalAuthorizationId", fixture.FiscalAuthorizationId);
            await command.ExecuteNonQueryAsync();
        }
        for (var index = 0; index < devices.Length; index++)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.EnrolledDevices(DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                    CredentialIterations,IsActive,CreatedAt)
                VALUES(@DeviceId,@TenantId,@Name,0x01,0x02,100000,1,SYSDATETIMEOFFSET());
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,
                    Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,@DeviceId,N'SalesInvoice',N'VTA',@Code,8,1,99999999,1,1,SYSDATETIMEOFFSET());
                """, seed);
            command.Parameters.AddWithValue("@DeviceId", devices[index]);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@Name", $"Concurrent {index + 1}");
            command.Parameters.AddWithValue("@Code", $"C{index + 1}");
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await Task.WhenAll(devices.Select(EnsureNumberingAsync));
            await using var verify = new SqlCommand("""
                SELECT COUNT(*),COUNT(DISTINCT DeviceId),MIN(RangeStart),MAX(RangeEnd)
                FROM dbo.FiscalSeries
                WHERE BusinessId=@BusinessId AND Prefix=N'FC' AND DeviceId IS NOT NULL
                  AND IsActive=1 AND AllocationState IN(N'Active',N'Standby');
                SELECT COUNT(*) FROM (
                  SELECT RangeStart,LAG(RangeEnd) OVER(ORDER BY RangeStart) PreviousEnd
                  FROM dbo.FiscalSeries
                  WHERE BusinessId=@BusinessId AND Prefix=N'FC' AND DeviceId IS NOT NULL
                ) ranges WHERE PreviousEnd>=RangeStart;
                """, seed);
            verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(6, reader.GetInt32(0));
            Assert.Equal(3, reader.GetInt32(1));
            Assert.Equal(60001, reader.GetInt64(2));
            Assert.Equal(60060, reader.GetInt64(3));
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
                DELETE dbo.FiscalSeries WHERE BusinessId=@BusinessId AND Prefix=N'FC';
                DELETE fiscal.FiscalNumberingPolicies WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice';
                DELETE dbo.DocumentSeries WHERE DeviceId IN(@Device1,@Device2,@Device3);
                DELETE dbo.EnrolledDevices WHERE DeviceId IN(@Device1,@Device2,@Device3);
                """, seed);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            cleanup.Parameters.AddWithValue("@Device1", devices[0]);
            cleanup.Parameters.AddWithValue("@Device2", devices[1]);
            cleanup.Parameters.AddWithValue("@Device3", devices[2]);
            await cleanup.ExecuteNonQueryAsync();
        }

        async Task EnsureNumberingAsync(Guid deviceId)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            await using var command = new SqlCommand("fiscal.FiscalDeviceNumberingEnsure", connection, transaction)
            { CommandType = System.Data.CommandType.StoredProcedure };
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            command.Parameters.AddWithValue("@CurrentSeriesId", DBNull.Value);
            command.Parameters.AddWithValue("@NextConsecutive", DBNull.Value);
            command.Parameters.AddWithValue("@ActiveSeriesId", Guid.NewGuid());
            command.Parameters.AddWithValue("@StandbySeriesId", Guid.NewGuid());
            command.Parameters.AddWithValue("@ActiveNotificationId", Guid.NewGuid());
            command.Parameters.AddWithValue("@StandbyNotificationId", Guid.NewGuid());
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }

    [Fact]
    public async Task Administrator_prepares_idempotent_active_and_standby_blocks_and_read_only_user_cannot_mutate_it()
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
            Assert.Equal(500, before.AvailableConsecutives);
            Assert.Contains(before.Devices, device =>
                device.DeviceId == fixture.DeniedDeviceId && !device.IsProvisioned);

            using var deniedResponse = await readOnly.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId));
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

            using var manager = fixture.CreateAdminClient(
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            using var assignedResponse = await manager.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId));
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
            var assigned = await assignedResponse.Content
                .ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>();
            Assert.NotNull(assigned);
            Assert.Equal(300, assigned.AvailableConsecutives);
            var deviceAssignment = Assert.Single(assigned.Devices, device =>
                device.DeviceId == fixture.DeniedDeviceId);
            Assert.True(deviceAssignment.IsProvisioned);
            Assert.Equal(30001, deviceAssignment.RangeStart);
            Assert.Equal(30100, deviceAssignment.RangeEnd);

            using var duplicateResponse = await manager.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/devices/assign?businessId={fixture.BusinessId:D}",
                new AssignFiscalDeviceSeriesRequest(fixture.DeniedDeviceId));
            Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
            var duplicate = await duplicateResponse.Content
                .ReadFromJsonAsync<FiscalDeviceSeriesWorkspace>();
            Assert.NotNull(duplicate);
            Assert.Equal(300, duplicate.AvailableConsecutives);

            Assert.Equal(2, await ScalarAsync(
                "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning'"));
        }
        finally
        {
            await ExecuteAsync("""
                DELETE dbo.PosSynchronizationOutboxMessages
                WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning'
                  ;
                DELETE dbo.FiscalSeries
                WHERE BusinessId=@BusinessId AND EmitterKind=N'Device'
                  AND (DeviceId=@DeviceId OR SeriesId IN(@PoolId,@ExpiredPoolId));
                DELETE fiscal.FiscalNumberingPolicies
                WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice';
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
               DocumentType,Prefix,RangeStart,RangeEnd,AllocationState,IsActive,CreatedAt)
            VALUES
              (@PoolId,@BusinessId,NULL,N'Device',@FiscalAuthorizationId,
               N'SalesInvoice',N'FV99',30001,30500,N'Pool',1,SYSDATETIMEOFFSET());
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
              DocumentType,Prefix,RangeStart,RangeEnd,AllocationState,IsActive,CreatedAt)
            VALUES(
              @ExpiredPoolId,@BusinessId,NULL,N'Device',@ExpiredAuthorizationId,
              N'SalesInvoice',N'OLD',40001,40020,N'Pool',1,SYSDATETIMEOFFSET());
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
