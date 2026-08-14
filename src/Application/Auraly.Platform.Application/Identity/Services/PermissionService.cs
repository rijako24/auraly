using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class PermissionService : IPermissionService
{
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

        foreach (var role in systemRoles)
        {
            var assignedPermissionIds = role.RolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();
            var missing = permissions
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

    private static PermissionDto MapToDto(Domain.Entities.Permission p) => new(
        p.PermissionId, p.Module, p.Action, p.Resource, p.Description);
}
