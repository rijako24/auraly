using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IRoleService
{
    Task<RoleDto> GetByIdAsync(Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> GetByTenantAsync(Guid? tenantId, CancellationToken ct = default);
    Task<PagedResponse<RoleDto>> GetPagedByTenantAsync(Guid? tenantId, PagedRequest request, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid roleId, CancellationToken ct = default);
    Task AssignPermissionsAsync(Guid roleId, AssignPermissionsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct = default);
}
