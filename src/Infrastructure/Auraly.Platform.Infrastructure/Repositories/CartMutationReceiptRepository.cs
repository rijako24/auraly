using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class CartMutationReceiptRepository : ICartMutationReceiptRepository
{
    private readonly ApplicationDbContext _context;
    public CartMutationReceiptRepository(ApplicationDbContext context) => _context = context;

    public Task<CartMutationReceipt?> GetAsync(
        Guid businessId, Guid conversationId, string idempotencyKey, CancellationToken ct = default) =>
        _context.CartMutationReceipts.AsNoTracking().FirstOrDefaultAsync(receipt =>
            receipt.BusinessId == businessId && receipt.ConversationId == conversationId
            && receipt.IdempotencyKey == idempotencyKey, ct);

    public Task<CartMutationReceipt> CreateAsync(CartMutationReceipt receipt, CancellationToken ct = default)
    {
        _context.CartMutationReceipts.Add(receipt);
        return Task.FromResult(receipt);
    }
}
