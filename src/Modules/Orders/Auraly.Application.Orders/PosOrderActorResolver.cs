using Auraly.Application.Sales;

namespace Auraly.Application.Orders;

public sealed record PosOrderExecutionContext(
    Guid UserId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId);

public interface IPosOrderActorResolver
{
    Task<OrderActor> ResolveAsync(
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken = default);
}