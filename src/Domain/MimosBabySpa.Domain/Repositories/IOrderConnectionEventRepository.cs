using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IOrderConnectionEventRepository
{
    Task<OrderConnectionEvent?> GetByOrderConnectionAsync(Guid orderId, Guid integrationConnectionId, CancellationToken ct = default);
    Task<OrderConnectionEvent> CreateAsync(OrderConnectionEvent entity, CancellationToken ct = default);
    Task<OrderConnectionEvent> UpdateAsync(OrderConnectionEvent entity, CancellationToken ct = default);
}
