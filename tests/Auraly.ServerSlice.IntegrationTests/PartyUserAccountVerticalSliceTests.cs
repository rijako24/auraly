using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PartyUserAccountVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Party_and_user_account_are_linked_without_moving_security_roles_to_party()
    {
        var countryId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var secondPartyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherTenantUserId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");

        await ExecuteAsync(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
              VALUES(@CountryId,N'ZZ',N'Integration country',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
               Identification,NormalizedIdentification,DisplayName,CompletionStatus,
               IsActive,CreatedBy,CreatedAt)
              VALUES
              (@PartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',@Identification,
               @Identification,N'Ada Operator',N'Complete',1,@ActorId,SYSDATETIMEOFFSET()),
              (@SecondPartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',@SecondIdentification,
               @SecondIdentification,N'Grace Operator',N'Complete',1,@ActorId,SYSDATETIMEOFFSET());
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
              VALUES
              (@UserId,@TenantId,@Username,@NormalizedUsername,@Email,@NormalizedEmail,
               N'Login',N'Projection',0,1,1,SYSUTCDATETIME()),
              (@SecondUserId,@TenantId,@SecondUsername,@SecondNormalizedUsername,
               @SecondEmail,@SecondNormalizedEmail,N'Second',N'Projection',0,1,1,SYSUTCDATETIME());
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
              VALUES(@OtherTenantId,N'Other tenant',@OtherTenantEmail,1,SYSUTCDATETIME());
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
              VALUES
              (@OtherTenantUserId,@OtherTenantId,@OtherUsername,@OtherNormalizedUsername,
               @OtherEmail,@OtherNormalizedEmail,N'Other',N'User',0,1,1,SYSUTCDATETIME());
            """,
            new SqlParameter("@CountryId", countryId),
            new SqlParameter("@PartyId", partyId),
            new SqlParameter("@SecondPartyId", secondPartyId),
            new SqlParameter("@TenantId", fixture.TenantId),
            new SqlParameter("@ActorId", fixture.UserId),
            new SqlParameter("@Identification", $"10{suffix[..14]}"),
            new SqlParameter("@SecondIdentification", $"20{suffix[..14]}"),
            new SqlParameter("@UserId", userId),
            new SqlParameter("@SecondUserId", secondUserId),
            new SqlParameter("@Username", $"party-user-{suffix}"),
            new SqlParameter("@NormalizedUsername", $"PARTY-USER-{suffix}"),
            new SqlParameter("@Email", $"party-user-{suffix}@auraly.test"),
            new SqlParameter("@NormalizedEmail", $"PARTY-USER-{suffix}@AURALY.TEST"),
            new SqlParameter("@SecondUsername", $"party-user-2-{suffix}"),
            new SqlParameter("@SecondNormalizedUsername", $"PARTY-USER-2-{suffix}"),
            new SqlParameter("@SecondEmail", $"party-user-2-{suffix}@auraly.test"),
            new SqlParameter("@SecondNormalizedEmail", $"PARTY-USER-2-{suffix}@AURALY.TEST"),
            new SqlParameter("@OtherTenantId", otherTenantId),
            new SqlParameter("@OtherTenantUserId", otherTenantUserId),
            new SqlParameter("@OtherTenantEmail", $"tenant-{suffix}@auraly.test"),
            new SqlParameter("@OtherUsername", $"other-user-{suffix}"),
            new SqlParameter("@OtherNormalizedUsername", $"OTHER-USER-{suffix}"),
            new SqlParameter("@OtherEmail", $"other-user-{suffix}@auraly.test"),
            new SqlParameter("@OtherNormalizedEmail", $"OTHER-USER-{suffix}@AURALY.TEST"));

        using var denied = fixture.CreateAdminClient(PartyPermissionCodes.Read);
        using var deniedResponse = await denied.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = fixture.CreateAdminClient(PartyPermissionCodes.ManageUserAccounts);
        using var linkedResponse = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.OK, linkedResponse.StatusCode);
        var linked = await linkedResponse.Content.ReadFromJsonAsync<PartyUserAccountLink>();
        Assert.NotNull(linked);
        Assert.Equal(partyId, linked.PartyId);
        Assert.Equal(userId, linked.UserId);

        var queried = await admin.GetFromJsonAsync<PartyUserAccountLink>(
            $"/api/commerce/v1/parties/{partyId:D}/user-account");
        Assert.NotNull(queried);
        Assert.Equal(userId, queried.UserId);

        using var idempotent = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.OK, idempotent.StatusCode);

        using var partyAlreadyLinked = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(secondUserId));
        Assert.Equal(HttpStatusCode.Conflict, partyAlreadyLinked.StatusCode);

        using var userAlreadyLinked = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{secondPartyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.Conflict, userAlreadyLinked.StatusCode);

        using var crossTenant = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{secondPartyId:D}/user-account",
            new LinkPartyUserAccountRequest(otherTenantUserId));
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        using var unlinked = await admin.DeleteAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account");
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
        Assert.Null(await ScalarAsync<Guid?>(
            "SELECT PartyId FROM dbo.AppUsers WHERE UserId=@UserId;",
            new SqlParameter("@UserId", userId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.AppUsers WHERE UserId=@UserId;",
            new SqlParameter("@UserId", userId)));
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T?> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }
}
