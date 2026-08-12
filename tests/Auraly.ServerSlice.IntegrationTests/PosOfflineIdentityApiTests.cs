using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosOfflineIdentityApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Device_downloads_only_the_separate_offline_verifier_and_effective_permissions()
    {
        const string password = "The-Same-Auraly-Password-1";
        var verifier = PosOfflinePasswordHasher.Hash(
            password, DateTimeOffset.UtcNow);
        await SeedCashierRoleAndVerifierAsync(verifier);

        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId:D}");
        request.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        request.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("MAIN-HASH-MUST-NOT-LEAK", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        var snapshot =
            await response.Content.ReadFromJsonAsync<PosOfflineIdentitySnapshot>();
        var cashier = Assert.Single(snapshot!.Users, user => user.UserId == fixture.UserId);
        Assert.Equal(fixture.UserId, cashier.UserId);
        Assert.Contains(CommercePermissionCodes.SalesCreate, cashier.Permissions);
        Assert.True(PosOfflinePasswordHasher.Verify(
            password, cashier.PasswordVerifier));
        Assert.False(PosOfflinePasswordHasher.Verify(
            "another-password", cashier.PasswordVerifier));
        Assert.True(snapshot.ValidUntil > snapshot.IssuedAt);
        Assert.Equal(64, snapshot.Revision.Length);
    }

    [Fact]
    public async Task Device_without_identity_sync_permission_is_denied()
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId:D}");
        request.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeniedDeviceId.ToString("D"));
        request.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeniedDeviceSecret);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedCashierRoleAndVerifierAsync(
        PosOfflinePasswordVerifier verifier)
    {
        var roleId = Guid.NewGuid();
        const string sql = """
            UPDATE dbo.AppUsers
            SET PasswordHash=N'MAIN-HASH-MUST-NOT-LEAK',
                PosOfflinePasswordSalt=@Salt,
                PosOfflinePasswordHash=@Hash,
                PosOfflinePasswordIterations=@Iterations,
                PosOfflinePasswordChangedAt=@ChangedAt
            WHERE UserId=@UserId;

            INSERT dbo.AppRoles(
                RoleId,TenantId,Name,NormalizedName,IsActive,CreatedAt)
            VALUES(
                @RoleId,@TenantId,N'Cajero identidad',@RoleName,1,SYSUTCDATETIME());
            INSERT dbo.UserRoles(
                UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(
                NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            INSERT dbo.RolePermissions(
                RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions
            WHERE Resource IN (
                N'sales.create',N'sales.discount',N'sales.reprint',N'sales.void');
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Salt", verifier.Salt);
        command.Parameters.AddWithValue("@Hash", verifier.Hash);
        command.Parameters.AddWithValue("@Iterations", verifier.Iterations);
        command.Parameters.AddWithValue("@ChangedAt", verifier.ChangedAt);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue(
            "@RoleName", $"CASHIER-IDENTITY-{roleId:N}");
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }
}
