using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Conversation
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public string UserNumber { get; set; } = string.Empty;
    public string? LastMessage { get; set; }
    public DateTime Timestamp { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    /// <summary>Atributos de identidad persistentes indexados por rol semántico (JSON).</summary>
    public string? IdentityAttributesJson { get; set; }
    public ConversationLifecycleStatus Status { get; set; } = ConversationLifecycleStatus.Active;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
    public virtual ICollection<ConversationContext> Contexts { get; set; } = new List<ConversationContext>();
}
