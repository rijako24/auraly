using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class OrderDraftItemRepository : IOrderDraftItemRepository
{
    private readonly ApplicationDbContext _context;

    public OrderDraftItemRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<OrderDraftItem>> GetByDraftIdAsync(Guid businessId, Guid orderDraftId, CancellationToken ct = default) =>
        await _context.OrderDraftItems
            .Where(i => i.BusinessId == businessId && i.OrderDraftId == orderDraftId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<OrderDraftItem?> GetByIdAsync(Guid businessId, Guid orderDraftItemId, CancellationToken ct = default) =>
        _context.OrderDraftItems.FirstOrDefaultAsync(i => i.BusinessId == businessId && i.OrderDraftItemId == orderDraftItemId, ct);

    public Task<OrderDraftItem> CreateAsync(OrderDraftItem item, CancellationToken ct = default)
    {
        _context.OrderDraftItems.Add(item);
        return Task.FromResult(item);
    }

    public Task<OrderDraftItem> UpdateAsync(OrderDraftItem item, CancellationToken ct = default)
    {
        _context.OrderDraftItems.Update(item);
        return Task.FromResult(item);
    }

    public Task DeleteAsync(OrderDraftItem item, CancellationToken ct = default)
    {
        _context.OrderDraftItems.Remove(item);
        return Task.CompletedTask;
    }
}
