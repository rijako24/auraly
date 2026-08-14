using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<AppUser?> GetByUsernameAsync(string normalizedUsername, CancellationToken ct = default);
    Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<AppUser?> GetByExternalLoginAsync(string provider, string providerKey, CancellationToken ct = default);
    Task<AppUser?> GetWithRolesAndPermissionsAsync(Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<AppUser> Items, int TotalCount)> GetPagedByTenantAsync(
        Guid tenantId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<bool> ExistsWithUsernameAsync(Guid tenantId, string normalizedUsername, Guid? excludeUserId = null, CancellationToken ct = default);
    Task<bool> ExistsWithEmailAsync(Guid tenantId, string normalizedEmail, Guid? excludeUserId = null, CancellationToken ct = default);
    Task AddAsync(AppUser user, CancellationToken ct = default);
    void Update(AppUser user);
}
