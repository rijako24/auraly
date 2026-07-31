using Auraly.Application.Sales;

namespace Auraly.Application.Orders;

public interface IPosOrderActorResolver
{
    Task<OrderActor> ResolveAsync(
        PosDeviceIdentity device,
        Guid userId,
        CancellationToken cancellationToken = default);
}
