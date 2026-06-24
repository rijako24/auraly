using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IOrderDraftItemRepository
{
    Task<IReadOnlyList<OrderDraftItem>> GetByDraftIdAsync(Guid businessId, Guid orderDraftId, CancellationToken ct = default);
    Task<OrderDraftItem?> GetByIdAsync(Guid businessId, Guid orderDraftItemId, CancellationToken ct = default);
    Task<OrderDraftItem> CreateAsync(OrderDraftItem item, CancellationToken ct = default);
    Task<OrderDraftItem> UpdateAsync(OrderDraftItem item, CancellationToken ct = default);
    Task DeleteAsync(OrderDraftItem item, CancellationToken ct = default);
}
