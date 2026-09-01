using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class PermissionService : IPermissionService
{
    private static readonly string[] OptInFeaturePermissionPrefixes =
    [
        "agents.", "conversations.", "leads.", "campaigns.",
        "reservations."
    ];
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(IUnitOfWork unitOfWork, ILogger<PermissionService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync(ct);
        return permissions.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> GetByModuleAsync(string module, CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetByModuleAsync(module, ct);
        return permissions.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PermissionDto>>> GetGroupedByModuleAsync(CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync(ct);
        return permissions
            .GroupBy(p => p.Module)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PermissionDto>)g.Select(MapToDto).ToList());
    }

    public async Task SeedPermissionsAsync(CancellationToken ct)
    {
        foreach (var (module, action, resource, description) in PermissionCatalog.All)
        {
            if (!await _unitOfWork.Permissions.ExistsByResourceAsync(resource, ct))
            {
                await _unitOfWork.Permissions.AddAsync(new Domain.Entities.Permission
                {
                    PermissionId = Guid.NewGuid(),
                    Module = module,
                    Action = action,
                    Resource = resource,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        await SyncSystemRolePermissionsAsync(ct);
        _logger.LogInformation("Permission seed completed. Total: {Count}", PermissionCatalog.All.Length);
    }

    private async Task SyncSystemRolePermissionsAsync(CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync(ct);
        var systemRoles = await _unitOfWork.AppRoles.GetActiveSystemRolesAsync(ct);

        // IsSystemRole protects built-in roles from being edited/deleted; it does not
        // mean that every built-in operational role is an administrator. Only the
        // administrator templates are allowed to receive the complete catalog.
        foreach (var role in systemRoles.Where(IsAdministratorRole))
        {
            var eligiblePermissions = permissions
                .Where(permission => IsAllowedForAdministrator(role, permission))
                .ToList();
            var eligiblePermissionIds = eligiblePermissions
                .Select(permission => permission.PermissionId)
                .ToHashSet();
            var improper = role.RolePermissions
                .Where(assignment => !eligiblePermissionIds.Contains(assignment.PermissionId))
                .ToList();
            if (improper.Count > 0)
                _unitOfWork.RolePermissions.DeleteRange(improper);

            var assignedPermissionIds = role.RolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();
            var missing = eligiblePermissions
                .Where(p => !assignedPermissionIds.Contains(p.PermissionId))
                .Select(p => new RolePermission
                {
                    RolePermissionId = Guid.NewGuid(),
                    RoleId = role.RoleId,
                    PermissionId = p.PermissionId,
                    AssignedAt = DateTime.UtcNow
                })
                .ToList();

            if (missing.Count > 0)
                await _unitOfWork.RolePermissions.AddRangeAsync(missing, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static bool IsAdministratorRole(AppRole role) =>
        role.NormalizedName is "ADMINISTRATOR" or "TENANTADMINISTRATOR";

    private static bool IsAllowedForAdministrator(
        AppRole role,
        Domain.Entities.Permission permission) =>
        string.Equals(
            role.Tenant?.TenantKey,
            PlatformPermissions.PlatformTenantKey,
            StringComparison.OrdinalIgnoreCase)
        || !permission.Resource.StartsWith("tenants.", StringComparison.OrdinalIgnoreCase)
          && !permission.Resource.StartsWith("platform.", StringComparison.OrdinalIgnoreCase)
          && !OptInFeaturePermissionPrefixes.Any(prefix =>
              permission.Resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static PermissionDto MapToDto(Domain.Entities.Permission p) => new(
        p.PermissionId, p.Module, p.Action, p.Resource, p.Description);
}
