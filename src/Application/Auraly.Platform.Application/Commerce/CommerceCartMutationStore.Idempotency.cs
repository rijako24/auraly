using System.Text.Json;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Application.Commerce;

public sealed partial class CommerceCartMutationStore
{
    public async Task<CartMutationApplyResult> ApplyIdempotentlyAsync(
        AgentConversationContext context,
        IReadOnlyList<ResolvedCartCommand> commands,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return new(await ApplyAtomicallyAsync(context, commands, cancellationToken), false);

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var receipt = await _unitOfWork.CartMutationReceipts.GetAsync(
                context.BusinessId, context.ConversationId, idempotencyKey, cancellationToken);
            if (receipt is not null)
            {
                var replayed = JsonSerializer.Deserialize<OrderSnapshot>(receipt.SnapshotJson)
                    ?? throw new InvalidOperationException("Stored cart mutation receipt has an invalid snapshot.");
                return new CartMutationApplyResult(replayed, true);
            }

            OrderSnapshot? snapshot = null;
            foreach (var command in commands)
            {
                snapshot = command.Operation switch
                {
                    CartCommandOperations.Add => await AddAsync(context, command, cancellationToken),
                    CartCommandOperations.Remove => await _commerce.RemoveItemAsync(context, command.OrderItemId!.Value, cancellationToken),
                    CartCommandOperations.SetQuantity => await _commerce.UpdateItemQuantityAsync(context, command.OrderItemId!.Value, command.Quantity!.Value, cancellationToken),
                    _ => throw new InvalidOperationException($"Unsupported resolved cart operation '{command.Operation}'.")
                };
            }
            snapshot ??= await _commerce.GetDraftAsync(context, cancellationToken);
            await _unitOfWork.CartMutationReceipts.CreateAsync(new CartMutationReceipt
            {
                CartMutationReceiptId = Guid.NewGuid(),
                BusinessId = context.BusinessId,
                ConversationId = context.ConversationId,
                IdempotencyKey = idempotencyKey,
                SnapshotJson = JsonSerializer.Serialize(snapshot),
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new CartMutationApplyResult(snapshot, false);
        }, cancellationToken);
    }
}
