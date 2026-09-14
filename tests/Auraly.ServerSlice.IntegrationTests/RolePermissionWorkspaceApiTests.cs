using System.Net;
using System.Net.Http.Json;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RolePermissionWorkspaceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Workspace_returns_role_catalog_and_assignments_in_one_authorized_request()
    {
        var roleId = await GetTenantRoleIdAsync();
        using var authorized = fixture.CreateTenantUserClient(
            fixture.TenantId,
            fixture.UserId,
            "roles.read",
            "permissions.read");

        using var response = await authorized.GetAsync(
            $"/api/v1/roles/{roleId:D}/permission-workspace");

        response.EnsureSuccessStatusCode();
        var workspace = await response.Content
            .ReadFromJsonAsync<RolePermissionWorkspaceDto>();
        Assert.NotNull(workspace);
        Assert.Equal(roleId, workspace.Role.RoleId);
        Assert.Equal(fixture.TenantId, workspace.Role.TenantId);
        Assert.NotEmpty(workspace.Permissions);

        using var missingCatalogPermission = fixture.CreateTenantUserClient(
            fixture.TenantId,
            fixture.UserId,
            "roles.read");
        using var forbidden = await missingCatalogPermission.GetAsync(
            $"/api/v1/roles/{roleId:D}/permission-workspace");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private async Task<Guid> GetTenantRoleIdAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) RoleId
            FROM dbo.AppRoles
            WHERE TenantId = @TenantId
            ORDER BY CreatedAt, RoleId;
            """;
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The test tenant has no roles."));
    }
}
