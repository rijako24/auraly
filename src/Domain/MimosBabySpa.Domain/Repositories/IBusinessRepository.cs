using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessRepository
{
    Task<Business?> GetByIdAsync(Guid businessId);
    Task<Business?> GetByIdWithConfigurationAsync(Guid businessId);
    Task<IReadOnlyList<Business>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<Business> Items, int TotalCount)> GetPagedByTenantIdAsync(
        Guid tenantId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Business> CreateAsync(Business business);
    Task<Business> UpdateAsync(Business business);
}
