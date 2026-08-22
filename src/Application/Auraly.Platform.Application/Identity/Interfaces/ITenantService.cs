using Auraly.Platform.Application.Common.DTOs;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ITenantService
{
    Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, Guid? actorUserId, CancellationToken ct = default);
    Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, int? maximumUsers, int? maximumEnrolledDevices, string? legalName = null, string? nit = null, string? verificationDigit = null, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, CancellationToken ct = default);
    Task ActivateAsync(Guid tenantId, CancellationToken ct = default);
}
