namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Almacena contexto dinámico de una conversación como strings simples.
/// Cada registro contiene un string de contexto extraído por la IA.
/// </summary>
public class ConversationContext
{
    public Guid ConversationContextId { get; set; }
    public Guid ConversationId { get; set; }
    public string Context { get; set; } = string.Empty; // String de contexto extraído por la IA
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public virtual Conversation Conversation { get; set; } = null!;
}
