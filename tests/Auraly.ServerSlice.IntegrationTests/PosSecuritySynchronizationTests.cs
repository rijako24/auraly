using System.Net.Http.Json;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosSecuritySynchronizationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task User_and_role_changes_enqueue_the_security_stream_for_the_tenant_business()
    {
        using var client = fixture.CreateAdminClient(
            "users.update",
            "roles.create",
            "roles.update");

        await ClearSecurityNotificationsAsync();
        using var userResponse = await client.PutAsJsonAsync(
            $"/api/v1/users/{fixture.UserId:D}",
            new UpdateUserRequest("Cajero sincronizado", null, null, null));
        userResponse.EnsureSuccessStatusCode();
        Assert.True(await CountSecurityNotificationsAsync() > 0);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/roles",
            new CreateRoleRequest(
                fixture.TenantId,
                $"Rol sincronizado {Guid.NewGuid():N}",
                "Regresión del stream Security"));
        createResponse.EnsureSuccessStatusCode();
        var role = await createResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);

        await ClearSecurityNotificationsAsync();
        using var roleResponse = await client.PutAsJsonAsync(
            $"/api/v1/roles/{role.RoleId:D}",
            new UpdateRoleRequest(role.Name, "Cambio que debe llegar al login local"));
        roleResponse.EnsureSuccessStatusCode();
        Assert.True(await CountSecurityNotificationsAsync() > 0);
    }

    private async Task ClearSecurityNotificationsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DELETE dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Security';
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountSecurityNotificationsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Security';
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
