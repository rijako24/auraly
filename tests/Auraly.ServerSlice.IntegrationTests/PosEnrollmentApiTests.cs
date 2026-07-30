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
    public async Task Authorized_user_enrolls_a_register_and_code_can_only_be_redeemed_once()
    {
        var registerId = Guid.NewGuid();
        var documentSeriesId = Guid.NewGuid();
        var fiscalSeriesId = Guid.NewGuid();
        await SeedOfflineRegisterAsync(registerId, documentSeriesId, fiscalSeriesId);
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.PosDevicesEnroll);

        using var authorizationResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId,
                fixture.LocationId,
                registerId,
                "Equipo recepción"));
        authorizationResponse.EnsureSuccessStatusCode();
        var authorization =
            await authorizationResponse.Content.ReadFromJsonAsync<PosEnrollmentAuthorization>();
        Assert.NotNull(authorization);
        Assert.Equal(registerId, authorization.Register.RegisterId);
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
        Assert.Equal(registerId, package.RegisterId);
        Assert.Equal("05", package.RegisterCode);
        Assert.NotEmpty(package.DeviceSecret);
        Assert.Equal(ServerSliceFixture.TechnicalKeyValue, package.FiscalSeries.TechnicalKey);
        Assert.Equal(documentSeriesId, package.DocumentSeries.SeriesId);
        Assert.Equal(fiscalSeriesId, package.FiscalSeries.SeriesId);
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
            FROM dbo.PosDevices WHERE DeviceId=@DeviceId AND RegisterId=@RegisterId;
            """, connection);
        command.Parameters.AddWithValue("@DeviceId", package.DeviceId);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    [Fact]
    public async Task User_without_enrollment_permission_is_denied()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/enrollments",
            new CreatePosEnrollmentRequest(
                fixture.BusinessId,
                fixture.LocationId,
                fixture.OnlineRegisterId,
                "Equipo no autorizado"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedOfflineRegisterAsync(
        Guid registerId,
        Guid documentSeriesId,
        Guid fiscalSeriesId)
    {
        const string sql = """
            INSERT dbo.CashRegisters
              (RegisterId,BusinessId,LocationId,WarehouseId,Code,Name,IsActive,CreatedAt)
            VALUES
              (@RegisterId,@BusinessId,@LocationId,@WarehouseId,N'05',
               N'Caja enrolamiento E2E',1,SYSDATETIMEOFFSET());
            INSERT dbo.DocumentSeries
              (DocumentSeriesId,BusinessId,LocationId,RegisterId,DocumentType,
               Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES
              (@DocumentSeriesId,@BusinessId,@LocationId,@RegisterId,N'Invoice',
               N'VTA',N'05',8,1,99999999,1,1,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalSeries
              (SeriesId,BusinessId,RegisterId,FiscalAuthorizationId,DocumentType,
               Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES
              (@FiscalSeriesId,@BusinessId,@RegisterId,@FiscalAuthorizationId,N'Invoice',
               @Prefix,20001,30000,1,SYSDATETIMEOFFSET());
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@LocationId", fixture.LocationId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@DocumentSeriesId", documentSeriesId);
        command.Parameters.AddWithValue("@FiscalSeriesId", fiscalSeriesId);
        command.Parameters.AddWithValue(
            "@FiscalAuthorizationId", fixture.FiscalAuthorizationId);
        command.Parameters.AddWithValue("@Prefix", ServerSliceFixture.Prefix);
        await command.ExecuteNonQueryAsync();
    }
}
