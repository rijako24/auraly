using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class OrderItemRepository : IOrderItemRepository
{
    private readonly ApplicationDbContext _context;

    public OrderItemRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<OrderItem>> GetByOrderIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default) =>
        await _context.OrderItems
            .Where(i => i.BusinessId == businessId && i.OrderId == orderId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<OrderItem?> GetByIdAsync(Guid businessId, Guid orderItemId, CancellationToken ct = default) =>
        _context.OrderItems.FirstOrDefaultAsync(i => i.BusinessId == businessId && i.OrderItemId == orderItemId, ct);

    public Task<OrderItem> CreateAsync(OrderItem item, CancellationToken ct = default)
    {
        _context.OrderItems.Add(item);
        return Task.FromResult(item);
    }

    public Task<OrderItem> UpdateAsync(OrderItem item, CancellationToken ct = default)
    {
        _context.OrderItems.Update(item);
        return Task.FromResult(item);
    }

    public Task DeleteAsync(OrderItem item, CancellationToken ct = default)
    {
        _context.OrderItems.Remove(item);
        return Task.CompletedTask;
    }
}
