using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface ITenantDeviceAdminStore
{
    Task<IReadOnlyList<TenantEnrolledDeviceDto>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid deviceId, CancellationToken ct = default);
}