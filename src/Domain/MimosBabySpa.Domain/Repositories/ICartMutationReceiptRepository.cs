using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface ICartMutationReceiptRepository
{
    Task<CartMutationReceipt?> GetAsync(Guid businessId, Guid conversationId, string idempotencyKey, CancellationToken ct = default);
    Task<CartMutationReceipt> CreateAsync(CartMutationReceipt receipt, CancellationToken ct = default);
}
