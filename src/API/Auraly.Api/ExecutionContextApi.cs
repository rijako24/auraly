using System.Data;
using System.Security.Claims;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace Auraly.Api;

public sealed record ExecutionTenantOption(Guid TenantId, string Name);
public sealed record ExecutionBusinessOption(Guid BusinessId, Guid TenantId, string Name);
public sealed record ExecutionAccess(
    Guid TenantId,
    Guid? BusinessId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public static class ExecutionContextApi
{
    public static IEndpointRouteBuilder MapExecutionContextApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/execution-context")
            .RequireAuthorization("authentication.user");

        group.MapGet("/tenants", async (
            ClaimsPrincipal user,
            SqlExecutionContextDirectory directory,
            CancellationToken cancellationToken) =>
            Results.Ok(await directory.ListTenantsAsync(
                RequiredUserId(user), cancellationToken)));

        group.MapGet("/businesses", async (
            ClaimsPrincipal user,
            SqlExecutionContextDirectory directory,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequiredGuid(user, "tenant_id");
            return Results.Ok(await directory.ListBusinessesAsync(
                RequiredUserId(user), tenantId, cancellationToken));
        });

        group.MapGet("/access", async (
            ClaimsPrincipal user,
            SqlExecutionContextDirectory directory,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequiredGuid(user, "tenant_id");
            var businessId = OptionalGuid(user, "business_id");
            var access = await directory.ResolveAccessAsync(
                RequiredUserId(user), tenantId, businessId, cancellationToken);
            return access.IsAllowed
                ? Results.Ok(new ExecutionAccess(
                    tenantId, businessId, access.Roles, access.Permissions))
                : Results.Forbid();
        });

        return endpoints;
    }

    private static Guid RequiredUserId(ClaimsPrincipal principal) =>
        RequiredGuid(principal, ClaimTypes.NameIdentifier);

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new UnauthorizedAccessException(
                $"The authenticated identity lacks claim '{claimType}'.");

    private static Guid? OptionalGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : null;
}

public sealed record ResolvedExecutionAccess(
    bool IsAllowed,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public interface IExecutionAccessResolver
{
    Task<ResolvedExecutionAccess> ResolveAccessAsync(
        Guid userId,
        Guid tenantId,
        Guid? businessId,
        CancellationToken cancellationToken);
}

public sealed class SqlExecutionContextDirectory(
    SqlServerConnectionFactory connections,
    IMemoryCache cache) : IExecutionAccessResolver
{
    public async Task<IReadOnlyList<ExecutionTenantOption>> ListTenantsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.ExecutionTenantsList", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        var result = new List<ExecutionTenantOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ExecutionTenantOption(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<ExecutionBusinessOption>> ListBusinessesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.ExecutionBusinessesList", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var result = new List<ExecutionBusinessOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ExecutionBusinessOption(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        return result;
    }

    public async Task<ResolvedExecutionAccess> ResolveAccessAsync(
        Guid userId,
        Guid tenantId,
        Guid? businessId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"execution-access:{userId:D}:{tenantId:D}:{businessId?.ToString("D") ?? "all"}";
        if (cache.TryGetValue(cacheKey, out ResolvedExecutionAccess? cached) && cached is not null)
            return cached;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.ExecutionAccessResolve", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.Add("@BusinessId", SqlDbType.UniqueIdentifier).Value =
            businessId is { } value ? value : DBNull.Value;

        var roles = new List<string>();
        var permissions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var target = reader.GetString(0) == "Role" ? roles : permissions;
            target.Add(reader.GetString(1));
        }
        var result = new ResolvedExecutionAccess(
            roles.Count > 0 || permissions.Count > 0, roles, permissions);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(10));
        return result;
    }

    private static SqlCommand Procedure(string name, SqlConnection connection) =>
        new(name, connection) { CommandType = CommandType.StoredProcedure };
}
