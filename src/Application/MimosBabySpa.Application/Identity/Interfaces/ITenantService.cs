using MimosBabySpa.Application.Common.DTOs;
using Auraly.Contracts.Tenants;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface ITenantService
{
    Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, Guid? actorUserId, CancellationToken ct = default);
    Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, int? maximumUsers, int? maximumEnrolledDevices, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, CancellationToken ct = default);
    Task ActivateAsync(Guid tenantId, CancellationToken ct = default);
}
