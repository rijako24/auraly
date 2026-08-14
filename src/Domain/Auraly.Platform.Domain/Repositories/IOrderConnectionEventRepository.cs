using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IOrderConnectionEventRepository
{
    Task<OrderConnectionEvent?> GetByOrderConnectionAsync(Guid orderId, Guid integrationConnectionId, CancellationToken ct = default);
    Task<OrderConnectionEvent> CreateAsync(OrderConnectionEvent entity, CancellationToken ct = default);
    Task<OrderConnectionEvent> UpdateAsync(OrderConnectionEvent entity, CancellationToken ct = default);
}
