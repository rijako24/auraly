using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IBusinessAdminService
{
    Task<BusinessDto> GetByIdAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDto>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<PagedResponse<BusinessDto>> GetPagedByTenantAsync(Guid tenantId, PagedRequest request, CancellationToken ct = default);
    Task<PagedResponse<BusinessDto>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<BusinessDto> CreateAsync(Guid tenantId, CreateBusinessRequest request, CancellationToken ct = default);
    Task<BusinessDto> UpdateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, UpdateBusinessRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct = default);
}
