using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByModuleAsync(string module, CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByRoleIdAsync(Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetResourcesByUserIdAsync(Guid userId, Guid? businessId = null, CancellationToken ct = default);
    Task<Permission?> GetByResourceAsync(string resource, CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByIdsAsync(IEnumerable<Guid> permissionIds, CancellationToken ct = default);
    Task AddAsync(Permission permission, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default);
    Task<bool> ExistsByResourceAsync(string resource, CancellationToken ct = default);
}
