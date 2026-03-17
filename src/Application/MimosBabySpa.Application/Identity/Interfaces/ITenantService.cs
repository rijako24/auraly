using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface ITenantService
{
    Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<TenantDto> CreateAsync(string name, string email, CancellationToken ct = default);
    Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, CancellationToken ct = default);
}
