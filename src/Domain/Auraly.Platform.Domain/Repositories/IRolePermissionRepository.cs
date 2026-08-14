using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IRolePermissionRepository
{
    Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(Guid roleId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default);
    void DeleteRange(IEnumerable<RolePermission> rolePermissions);
}
