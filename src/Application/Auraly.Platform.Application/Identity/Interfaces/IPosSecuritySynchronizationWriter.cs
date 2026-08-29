namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IPosSecuritySynchronizationWriter
{
    Task EnqueueTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
