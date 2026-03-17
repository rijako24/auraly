using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAppRoleRepository
{
    Task<AppRole?> GetByIdAsync(Guid roleId, CancellationToken ct = default);
    Task<AppRole?> GetWithPermissionsAsync(Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<AppRole>> GetByTenantAsync(Guid? tenantId, bool includeSystemRoles = true, CancellationToken ct = default);
    Task<(IReadOnlyList<AppRole> Items, int TotalCount)> GetPagedByTenantAsync(
        Guid? tenantId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<bool> ExistsWithNameAsync(Guid? tenantId, string normalizedName, Guid? excludeRoleId = null, CancellationToken ct = default);
    Task AddAsync(AppRole role, CancellationToken ct = default);
    void Update(AppRole role);
    void Delete(AppRole role);
}
