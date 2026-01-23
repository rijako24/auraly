using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Conversation
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public string UserNumber { get; set; } = string.Empty;
    public string? LastMessage { get; set; }
    public string? LastIntent { get; set; }
    public DateTime Timestamp { get; set; }
    public string? CustomerName { get; set; }
    public int? BabyAge { get; set; }
    public string? RecommendedPlan { get; set; }
    
    /// <summary>
    /// Estado explícito de la conversación para flujo determinístico
    /// </summary>
    public ConversationState State { get; set; } = ConversationState.Idle;
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
    public virtual ICollection<ConversationContext> Contexts { get; set; } = new List<ConversationContext>();
}
