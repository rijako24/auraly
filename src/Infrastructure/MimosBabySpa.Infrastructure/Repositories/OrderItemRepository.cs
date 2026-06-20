using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
