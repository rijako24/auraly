namespace MimosBabySpa.Domain.Entities;

public class Conversation
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Agente asociado al canal (p. ej. WhatsApp) de esta conversación. Permite resolver
    /// <see cref="FlowExecutionStateEntity"/> sin iterar todos los agentes del negocio.
    /// </summary>
    public Guid? AgentId { get; set; }

    public string UserNumber { get; set; } = string.Empty;

    /// <summary>
    /// Texto del último mensaje entrante del usuario (actualizado al persistir mensajes con sender User).
    /// </summary>
    public string? LastMessage { get; set; }

    public DateTime Timestamp { get; set; }
    public string? CustomerName { get; set; }

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Agent? Agent { get; set; }
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
    public virtual ICollection<ConversationContext> Contexts { get; set; } = new List<ConversationContext>();
}
