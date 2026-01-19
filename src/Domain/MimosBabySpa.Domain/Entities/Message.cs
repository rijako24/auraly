namespace MimosBabySpa.Domain.Entities;

public class Message
{
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public string Sender { get; set; } = string.Empty; // "User" or "Bot"
    public string MessageText { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    
    // Navigation property
    public virtual Conversation Conversation { get; set; } = null!;
}
