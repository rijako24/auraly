namespace Auraly.Platform.Domain.Entities;

public sealed class CartMutationReceipt
{
    public Guid CartMutationReceiptId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ConversationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public Conversation Conversation { get; set; } = null!;
}
