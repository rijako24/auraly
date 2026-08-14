using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ITenantDeviceAdminStore
{
    Task<IReadOnlyList<TenantEnrolledDeviceDto>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid deviceId, CancellationToken ct = default);
}