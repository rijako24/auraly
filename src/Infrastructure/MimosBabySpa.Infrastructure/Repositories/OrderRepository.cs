using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;

    public OrderRepository(ApplicationDbContext context) => _context = context;

    public Task<Order?> GetByIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default) =>
        _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.OrderId == orderId, ct);

    public Task<Order?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default) =>
        _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.PaymentTransactionId == paymentTransactionId, ct);

    public Task<Order?> GetActiveDraftByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        _context.Orders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == businessId && o.ConversationId == conversationId)
            .Where(o => o.Status == OrderStatus.Draft
                || o.Status == OrderStatus.PendingConfirmation
                || o.Status == OrderStatus.AwaitingPayment)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Order>> GetByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == businessId && o.ConversationId == conversationId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public Task<Order> CreateAsync(Order order, CancellationToken ct = default)
    {
        _context.Orders.Add(order);
        return Task.FromResult(order);
    }

    public Task<Order> UpdateAsync(Order order, CancellationToken ct = default)
    {
        _context.Orders.Update(order);
        return Task.FromResult(order);
    }
}
