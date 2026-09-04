namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IPosPricingSynchronizationWriter
{
    Task EnqueueBusinessesAsync(
        IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken = default);
}
