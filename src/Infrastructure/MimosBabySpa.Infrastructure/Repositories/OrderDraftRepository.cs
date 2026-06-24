using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class OrderDraftRepository : IOrderDraftRepository
{
    private readonly ApplicationDbContext _context;

    public OrderDraftRepository(ApplicationDbContext context) => _context = context;

    public Task<OrderDraft?> GetActiveByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        _context.OrderDrafts
            .Include(d => d.Items)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(d =>
                d.BusinessId == businessId &&
                d.ConversationId == conversationId,
                ct);

    public Task<OrderDraft?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default) =>
        _context.OrderDrafts
            .Include(d => d.Items)
            .FirstOrDefaultAsync(d =>
                d.BusinessId == businessId &&
                d.PaymentTransactionId == paymentTransactionId,
                ct);

    public async Task<IReadOnlyList<OrderDraft>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        await _context.OrderDrafts
            .Include(d => d.Items)
            .Where(d => d.BusinessId == businessId && d.ConversationId == conversationId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<OrderDraft> CreateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        _context.OrderDrafts.Add(draft);
        return Task.FromResult(draft);
    }

    public Task DeleteAsync(OrderDraft draft, CancellationToken ct = default)
    {
        _context.OrderDrafts.Remove(draft);
        return Task.CompletedTask;
    }

    public Task<OrderDraft> UpdateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        _context.OrderDrafts.Update(draft);
        return Task.FromResult(draft);
    }
}