using Auraly.Contracts.Authorization;

namespace Auraly.Application.Authorization;

public sealed record PosIdentityDeviceScope(
    Guid DeviceId,
    Guid TenantId,
    Guid BusinessId);

public interface IPosOfflineIdentityStore
{
    Task<PosOfflineIdentitySnapshot> SnapshotAsync(
        PosIdentityDeviceScope device,
        CancellationToken cancellationToken);
}

public sealed class PosIdentityForbiddenException(string message) : Exception(message);

public sealed class PosOfflineIdentityService(IPosOfflineIdentityStore store)
{
    public Task<PosOfflineIdentitySnapshot> SnapshotAsync(
        PosIdentityDeviceScope device,
        CancellationToken cancellationToken = default)
    {
        if (device.DeviceId == Guid.Empty || device.TenantId == Guid.Empty ||
            device.BusinessId == Guid.Empty)
        {
            throw new PosIdentityForbiddenException(
                "La identidad del dispositivo POS está incompleta.");
        }

        return store.SnapshotAsync(device, cancellationToken);
    }
}
