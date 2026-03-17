using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class RoleService : IRoleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        IUnitOfWork unitOfWork,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<RoleService> logger)
    {
        _unitOfWork = unitOfWork;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<RoleDto> GetByIdAsync(Guid roleId, CancellationToken ct)
    {
        var role = await _unitOfWork.AppRoles.GetWithPermissionsAsync(roleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), roleId);

        return MapToDto(role);
    }

    public async Task<IReadOnlyList<RoleDto>> GetByTenantAsync(Guid? tenantId, CancellationToken ct)
    {
        var roles = await _unitOfWork.AppRoles.GetByTenantAsync(tenantId, includeSystemRoles: true, ct);
        return roles.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<RoleDto>> GetPagedByTenantAsync(
        Guid? tenantId, PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.AppRoles.GetPagedByTenantAsync(
            tenantId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<RoleDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct)
    {
        var normalizedName = request.Name.ToUpperInvariant();

        if (await _unitOfWork.AppRoles.ExistsWithNameAsync(request.TenantId, normalizedName, ct: ct))
            throw new ConflictException($"Ya existe un rol con el nombre '{request.Name}'.");

        var role = new AppRole
        {
            RoleId = Guid.NewGuid(),
            TenantId = request.TenantId,
            Name = request.Name,
            NormalizedName = normalizedName,
            Description = request.Description,
            IsSystemRole = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.AppRoles.AddAsync(role, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Role '{RoleName}' created [CorrelationId: {CorrelationId}]",
            role.Name, _correlationIdProvider.CorrelationId);

        return MapToDto(role);
    }

    public async Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        var role = await _unitOfWork.AppRoles.GetByIdAsync(roleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), roleId);

        if (role.IsSystemRole)
            throw new ForbiddenException("No se pueden modificar roles de sistema.");

        if (request.Name is not null)
        {
            var normalizedName = request.Name.ToUpperInvariant();
            if (await _unitOfWork.AppRoles.ExistsWithNameAsync(role.TenantId, normalizedName, roleId, ct))
                throw new ConflictException($"Ya existe un rol con el nombre '{request.Name}'.");

            role.Name = request.Name;
            role.NormalizedName = normalizedName;
        }

        if (request.Description is not null)
            role.Description = request.Description;

        role.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AppRoles.Update(role);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(role);
    }

    public async Task DeactivateAsync(Guid roleId, CancellationToken ct)
    {
        var role = await _unitOfWork.AppRoles.GetByIdAsync(roleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), roleId);

        if (role.IsSystemRole)
            throw new ForbiddenException("No se pueden desactivar roles de sistema.");

        role.IsActive = false;
        role.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AppRoles.Update(role);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task AssignPermissionsAsync(Guid roleId, AssignPermissionsRequest request, CancellationToken ct)
    {
        var role = await _unitOfWork.AppRoles.GetWithPermissionsAsync(roleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), roleId);

        if (role.IsSystemRole)
            throw new ForbiddenException("No se pueden modificar permisos de roles de sistema.");

        var existingPermissions = await _unitOfWork.RolePermissions.GetByRoleIdAsync(roleId, ct);
        _unitOfWork.RolePermissions.DeleteRange(existingPermissions);

        var permissions = await _unitOfWork.Permissions.GetByIdsAsync(request.PermissionIds, ct);

        var newRolePermissions = permissions.Select(p => new RolePermission
        {
            RolePermissionId = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = p.PermissionId,
            AssignedAt = DateTime.UtcNow
        });

        await _unitOfWork.RolePermissions.AddRangeAsync(newRolePermissions, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Permissions updated for role {RoleId}: {Count} permissions [CorrelationId: {CorrelationId}]",
            roleId, permissions.Count, _correlationIdProvider.CorrelationId);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetByRoleIdAsync(roleId, ct);
        return permissions.Select(p => new PermissionDto(
            p.PermissionId, p.Module, p.Action, p.Resource, p.Description)).ToList();
    }

    private static RoleDto MapToDto(AppRole role) => new(
        role.RoleId, role.TenantId, role.Name, role.Description,
        role.IsSystemRole, role.IsActive, role.CreatedAt,
        role.UserRoles.Count, role.RolePermissions.Count);
}
