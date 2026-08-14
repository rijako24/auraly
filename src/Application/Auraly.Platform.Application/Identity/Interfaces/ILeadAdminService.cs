using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ILeadAdminService
{
    Task<LeadDto> GetByIdAsync(Guid tenantId, Guid leadId, CancellationToken ct = default);
    Task<IReadOnlyList<LeadDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<PagedResponse<LeadDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<LeadDto> CreateAsync(Guid tenantId, CreateLeadRequest request, CancellationToken ct = default);
    Task<LeadDto> UpdateAsync(Guid tenantId, Guid leadId, UpdateLeadRequest request, CancellationToken ct = default);
}
