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

    public async Task<Order?> GetActiveDraftByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        (await GetActiveDraftsByConversationAsync(businessId, conversationId, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<Order>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == businessId && o.ConversationId == conversationId)
            .Where(o => o.Status == OrderStatus.Draft
                || o.Status == OrderStatus.PendingConfirmation
                || o.Status == OrderStatus.AwaitingPayment)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == businessId && o.ConversationId == conversationId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(businessId, search, customer, createdFrom, createdTo, status);
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(int TotalOrders, decimal TotalAmount, int DraftCount, int AwaitingPaymentCount, int ConfirmedCount, int SyncedCount, int CancelledCount)> GetSummaryByBusinessIdAsync(
        Guid businessId,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(businessId, search, customer, createdFrom, createdTo, status);

        var totalOrders = await query.CountAsync(ct);
        var totalAmount = totalOrders == 0 ? 0 : await query.SumAsync(o => o.Total, ct);
        var draftCount = await query.CountAsync(o => o.Status == OrderStatus.Draft, ct);
        var awaitingPaymentCount = await query.CountAsync(o => o.Status == OrderStatus.AwaitingPayment, ct);
        var confirmedCount = await query.CountAsync(o => o.Status == OrderStatus.Confirmed, ct);
        var syncedCount = await query.CountAsync(o => o.Status == OrderStatus.Synced, ct);
        var cancelledCount = await query.CountAsync(o => o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Expired, ct);

        return (totalOrders, totalAmount, draftCount, awaitingPaymentCount, confirmedCount, syncedCount, cancelledCount);
    }

    private IQueryable<Order> BuildFilteredQuery(
        Guid businessId,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status)
    {
        var query = _context.Orders.Where(o => o.BusinessId == businessId);

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        if (createdFrom.HasValue)
            query = query.Where(o => o.CreatedAt >= createdFrom.Value.Date);

        if (createdTo.HasValue)
            query = query.Where(o => o.CreatedAt < createdTo.Value.Date.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var isOrderId = Guid.TryParse(term, out var orderId);
            query = query.Where(o =>
                (isOrderId && o.OrderId == orderId) ||
                (o.ExternalOrderId != null && o.ExternalOrderId.Contains(term)) ||
                (o.ExternalDocumentNumber != null && o.ExternalDocumentNumber.Contains(term)) ||
                o.Items.Any(i => i.ProductNameSnapshot.Contains(term) || (i.Sku != null && i.Sku.Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(customer))
        {
            var term = customer.Trim();
            query = query.Where(o =>
                (o.CustomerNameSnapshot != null && o.CustomerNameSnapshot.Contains(term)) ||
                (o.CustomerEmailSnapshot != null && o.CustomerEmailSnapshot.Contains(term)) ||
                (o.CustomerPhoneSnapshot != null && o.CustomerPhoneSnapshot.Contains(term)) ||
                (o.CustomerDocumentSnapshot != null && o.CustomerDocumentSnapshot.Contains(term)));
        }

        return query;
    }

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
