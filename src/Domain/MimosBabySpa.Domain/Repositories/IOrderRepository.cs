using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default);
    Task<Order?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default);
    Task<Order?> GetActiveDraftByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default);
    Task<(int TotalOrders, decimal TotalAmount, int DraftCount, int AwaitingPaymentCount, int ConfirmedCount, int SyncedCount, int CancelledCount)> GetSummaryByBusinessIdAsync(
        Guid businessId,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default);
    Task<Order> CreateAsync(Order order, CancellationToken ct = default);
    Task<Order> UpdateAsync(Order order, CancellationToken ct = default);
}
