using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<Tenant?> GetByIdForCapacityUpdateAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    void Update(Tenant tenant);
    Task RevokeActiveAuthenticationSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default);
    Task UpdateLegalIdentityAsync(Guid tenantId, string legalName, string nit, string verificationDigit, DateTimeOffset now, CancellationToken ct = default);
}
