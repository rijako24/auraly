using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IServiceAdminService
{
    Task<ServiceDto> GetByIdAsync(Guid tenantId, Guid serviceId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<PagedResponse<ServiceDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<PagedResponse<ServiceCategoryDto>> GetPagedCategoriesByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<ServiceDto> CreateAsync(Guid tenantId, CreateServiceRequest request, CancellationToken ct = default);
    Task<ServiceDto> UpdateAsync(Guid tenantId, Guid serviceId, UpdateServiceRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid serviceId, CancellationToken ct = default);
}
