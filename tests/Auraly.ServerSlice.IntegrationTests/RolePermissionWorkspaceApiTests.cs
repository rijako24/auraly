using System.Net;
using System.Net.Http.Json;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RolePermissionWorkspaceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Create_and_update_save_identity_and_permissions_atomically()
    {
        var permissionIds = await GetPermissionIdsAsync("roles.read", "permissions.read");
        await GrantActorPermissionsAsync(permissionIds.Values);
        using var authorized = fixture.CreateAdminClient(
            "roles.create",
            "roles.update");
        var name = $"Rol atómico {Guid.NewGuid():N}";

        using var create = await authorized.PostAsJsonAsync(
            "/api/v1/roles",
            new CreateRoleRequest(
                fixture.TenantId,
                name,
                "Descripción inicial",
                permissionIds.Values.ToArray()));

        create.EnsureSuccessStatusCode();
        var created = Assert.IsType<RoleDto>(
            await create.Content.ReadFromJsonAsync<RoleDto>());
        Assert.Equal(2, created.PermissionCount);
        Assert.Equal(permissionIds.Values.Order(),
            (await GetAssignedPermissionIdsAsync(created.RoleId)).Order());

        using var update = await authorized.PutAsJsonAsync(
            $"/api/v1/roles/{created.RoleId:D}",
            new UpdateRoleRequest(
                $"{name} actualizado",
                null,
                [permissionIds["roles.read"]]));

        update.EnsureSuccessStatusCode();
        var updated = Assert.IsType<RoleDto>(
            await update.Content.ReadFromJsonAsync<RoleDto>());
        Assert.Equal($"{name} actualizado", updated.Name);
        Assert.Null(updated.Description);
        Assert.Equal(1, updated.PermissionCount);
        Assert.Equal(
            permissionIds["roles.read"],
            Assert.Single(await GetAssignedPermissionIdsAsync(created.RoleId)));

        using var forbiddenClient = fixture.CreateAdminClient("roles.read");
        using var forbidden = await forbiddenClient.PutAsJsonAsync(
            $"/api/v1/roles/{created.RoleId:D}",
            new UpdateRoleRequest("No autorizado", null, []));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

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

    private async Task<Dictionary<string, Guid>> GetPermissionIdsAsync(params string[] resources)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Resource,PermissionId
            FROM dbo.Permissions
            WHERE Resource IN (N'roles.read',N'permissions.read');
            """;
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0), reader.GetGuid(1));
        Assert.All(resources, resource => Assert.True(result.ContainsKey(resource)));
        return result;
    }

    private async Task<Guid[]> GetAssignedPermissionIdsAsync(Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PermissionId
            FROM dbo.RolePermissions
            WHERE RoleId=@RoleId
            ORDER BY PermissionId;
            """;
        command.Parameters.AddWithValue("@RoleId", roleId);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetGuid(0));
        return result.ToArray();
    }

    private async Task GrantActorPermissionsAsync(IEnumerable<Guid> permissionIds)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),candidate.RoleId,candidate.PermissionId,SYSUTCDATETIME()
            FROM(
              SELECT DISTINCT userRole.RoleId,permissionValue.PermissionId
              FROM dbo.UserRoles userRole
            CROSS JOIN dbo.Permissions permissionValue
            WHERE userRole.UserId=@UserId
              AND permissionValue.PermissionId IN (SELECT value FROM STRING_SPLIT(@PermissionIds,','))
            ) candidate
            WHERE NOT EXISTS(
                  SELECT 1 FROM dbo.RolePermissions existing
                  WHERE existing.RoleId=candidate.RoleId
                    AND existing.PermissionId=candidate.PermissionId);
            """;
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue(
            "@PermissionIds",
            string.Join(',', permissionIds.Select(value => value.ToString("D"))));
        await command.ExecuteNonQueryAsync();
    }
}
