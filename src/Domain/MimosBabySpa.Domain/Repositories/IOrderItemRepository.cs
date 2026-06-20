using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IOrderItemRepository
{
    Task<IReadOnlyList<OrderItem>> GetByOrderIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default);
    Task<OrderItem?> GetByIdAsync(Guid businessId, Guid orderItemId, CancellationToken ct = default);
    Task<OrderItem> CreateAsync(OrderItem item, CancellationToken ct = default);
    Task<OrderItem> UpdateAsync(OrderItem item, CancellationToken ct = default);
    Task DeleteAsync(OrderItem item, CancellationToken ct = default);
}
