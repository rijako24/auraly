using Microsoft.Extensions.Logging;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class RoleService : IRoleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<RoleService> _logger;
    private readonly IPosSecuritySynchronizationWriter _securitySynchronization;
    private readonly IPosSynchronizationOutboxDispatcher _synchronization;

    public RoleService(
        IUnitOfWork unitOfWork,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<RoleService> logger,
        IPosSecuritySynchronizationWriter securitySynchronization,
        IPosSynchronizationOutboxDispatcher synchronization)
    {
        _unitOfWork = unitOfWork;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
        _securitySynchronization = securitySynchronization;
        _synchronization = synchronization;
    }

    public async Task<RoleDto> GetByIdAsync(Guid roleId, CancellationToken ct)
    {
        var role = await GetRoleAsync(roleId, includePermissions: true, ct);
        return MapToDto(role);
    }

    public async Task<IReadOnlyList<RoleDto>> GetByTenantAsync(Guid? tenantId, CancellationToken ct)
    {
        var roles = await _unitOfWork.AppRoles.GetByTenantAsync(tenantId, includeSystemRoles: true, ct);
        return roles.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<RoleDto>> GetPagedByTenantAsync(Guid? tenantId, PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.AppRoles.GetPagedByTenantAsync(tenantId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<RoleDto>(items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<RoleDto> CreateAsync(Guid tenantId, CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var normalizedName = request.Name.ToUpperInvariant();
            if (await _unitOfWork.AppRoles.ExistsWithNameAsync(tenantId, normalizedName, ct: ct))
                throw new ConflictException($"Ya existe un rol con el nombre '{request.Name}'.");

            var role = new AppRole
            {
                RoleId = Guid.NewGuid(), TenantId = tenantId, Name = request.Name,
                NormalizedName = normalizedName, Description = request.Description,
                IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.AppRoles.AddAsync(role, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(tenantId, ct);
            _logger.LogInformation("Role '{RoleName}' created [CorrelationId: {CorrelationId}]", role.Name, _correlationIdProvider.CorrelationId);
            return MapToDto(role);
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
        return result;
    }

    public async Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var role = await GetRoleAsync(roleId, includePermissions: false, ct);
            EnsureMutable(role);
            if (request.Name is not null)
            {
                var normalizedName = request.Name.ToUpperInvariant();
                if (await _unitOfWork.AppRoles.ExistsWithNameAsync(role.TenantId, normalizedName, roleId, ct))
                    throw new ConflictException($"Ya existe un rol con el nombre '{request.Name}'.");
                role.Name = request.Name;
                role.NormalizedName = normalizedName;
            }
            if (request.Description is not null) role.Description = request.Description;
            role.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppRoles.Update(role);
            await _unitOfWork.SaveChangesAsync(ct);
            var tenantId = role.TenantId!.Value;
            await _securitySynchronization.EnqueueTenantAsync(tenantId, ct);
            return (Value: MapToDto(role), TenantId: tenantId);
        }, ct);
        await DispatchSecurityAsync(result.TenantId, ct);
        return result.Value;
    }

    public async Task DeactivateAsync(Guid roleId, CancellationToken ct)
    {
        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var role = await GetRoleAsync(roleId, includePermissions: false, ct);
            EnsureMutable(role);
            role.IsActive = false;
            role.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppRoles.Update(role);
            await _unitOfWork.SaveChangesAsync(ct);
            var tenantId = role.TenantId!.Value;
            await _securitySynchronization.EnqueueTenantAsync(tenantId, ct);
            return tenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }

    public async Task AssignPermissionsAsync(Guid roleId, AssignPermissionsRequest request, Guid actorUserId, CancellationToken ct)
    {
        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var role = await GetRoleAsync(roleId, includePermissions: true, ct);
            EnsureMutable(role);
            var actor = await _unitOfWork.AppUsers.GetByIdAsync(actorUserId, ct)
                ?? throw new NotFoundException(nameof(AppUser), actorUserId);
            if (role.TenantId != actor.TenantId)
                throw new ForbiddenException("No puede administrar roles de otra organización.");

            var requestedIds = request.PermissionIds.Distinct().ToArray();
            var permissions = await _unitOfWork.Permissions.GetByIdsAsync(requestedIds, ct);
            if (permissions.Count != requestedIds.Length)
                throw new DomainValidationException("permissionIds", "Uno o más permisos no existen.");

            var actorPermissions = (await _unitOfWork.Permissions.GetResourcesByUserIdAsync(actorUserId, null, ct)).ToHashSet(StringComparer.Ordinal);
            var unauthorized = permissions.FirstOrDefault(permission => !actorPermissions.Contains(permission.Resource));
            if (unauthorized is not null)
                throw new ForbiddenException($"No puede delegar el permiso '{unauthorized.Resource}' porque no lo posee.");

            if (permissions.Any(permission => PlatformPermissions.IsPlatformPermission(permission.Resource)))
            {
                var tenant = await _unitOfWork.Tenants.GetByIdAsync(actor.TenantId, ct)
                    ?? throw new NotFoundException(nameof(Tenant), actor.TenantId);
                if (!string.Equals(tenant.TenantKey, PlatformPermissions.PlatformTenantKey, StringComparison.OrdinalIgnoreCase)
                    || !actorPermissions.Contains(PlatformPermissions.Assign))
                    throw new ForbiddenException("Los permisos de plataforma solo se pueden delegar dentro de @auraly por un usuario autorizado.");
            }
            if (permissions.Any(permission => PlatformPermissions.IsNonDelegable(permission.Resource)))
                throw new ForbiddenException("Este permiso es exclusivo del administrador general de Auraly y no se puede delegar.");

            var existingPermissions = await _unitOfWork.RolePermissions.GetByRoleIdAsync(roleId, ct);
            _unitOfWork.RolePermissions.DeleteRange(existingPermissions);
            await _unitOfWork.RolePermissions.AddRangeAsync(permissions.Select(permission => new RolePermission
            {
                RolePermissionId = Guid.NewGuid(), RoleId = roleId,
                PermissionId = permission.PermissionId, AssignedAt = DateTime.UtcNow
            }), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            var roleTenantId = role.TenantId!.Value;
            await _securitySynchronization.EnqueueTenantAsync(roleTenantId, ct);
            _logger.LogInformation("Permissions updated for role {RoleId}: {Count} permissions [CorrelationId: {CorrelationId}]", roleId, permissions.Count, _correlationIdProvider.CorrelationId);
            return roleTenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetByRoleIdAsync(roleId, ct);
        return permissions.Select(p => new PermissionDto(p.PermissionId, p.Module, p.Action, p.Resource, p.Description)).ToList();
    }

    private async Task<AppRole> GetRoleAsync(Guid roleId, bool includePermissions, CancellationToken ct) =>
        (includePermissions
            ? await _unitOfWork.AppRoles.GetWithPermissionsAsync(roleId, ct)
            : await _unitOfWork.AppRoles.GetByIdAsync(roleId, ct))
        ?? throw new NotFoundException(nameof(AppRole), roleId);

    private static void EnsureMutable(AppRole role)
    {
        if (role.IsSystemRole) throw new ForbiddenException("No se pueden modificar roles de sistema.");
    }

    private async Task DispatchSecurityAsync(Guid tenantId, CancellationToken ct)
    {
        var businesses = await _unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct);
        foreach (var business in businesses.Where(item => item.IsActive))
            await _synchronization.DispatchPendingAsync(
                tenantId, business.BusinessId, CancellationToken.None);
    }

    private static RoleDto MapToDto(AppRole role) => new(
        role.RoleId, role.TenantId, role.Name, role.Description,
        role.IsSystemRole, role.IsActive, role.CreatedAt,
        role.UserRoles.Count, role.RolePermissions.Count);
}
