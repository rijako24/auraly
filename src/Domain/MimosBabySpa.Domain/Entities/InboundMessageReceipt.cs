namespace MimosBabySpa.Domain.Entities;

public class InboundMessageReceipt
{
    public Guid InboundMessageReceiptId { get; set; }
    public Guid BusinessId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime ProcessingStartedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? LastError { get; set; }

    public virtual Business Business { get; set; } = null!;
}
