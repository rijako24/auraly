namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IPosSecuritySynchronizationWriter
{
    Task EnqueueUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task EnqueueRoleUsersAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken = default);
}
