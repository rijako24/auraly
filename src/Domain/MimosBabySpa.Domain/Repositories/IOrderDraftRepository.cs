using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IOrderDraftRepository
{
    Task<OrderDraft?> GetActiveByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default);
    Task<OrderDraft?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderDraft>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default);
    Task<OrderDraft> CreateAsync(OrderDraft draft, CancellationToken ct = default);
    Task DeleteAsync(OrderDraft draft, CancellationToken ct = default);
    Task<OrderDraft> UpdateAsync(OrderDraft draft, CancellationToken ct = default);
}