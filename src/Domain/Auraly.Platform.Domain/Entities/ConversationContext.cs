namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Almacena contexto importante de una conversación extraído por la IA.
/// Se llena cuando SmartExtractionService extrae información del mensaje del usuario.
/// </summary>
public class ConversationContext
{
    public Guid ConversationContextId { get; set; }
    public Guid ConversationId { get; set; }
    public string Field { get; set; } = string.Empty; // Campo de información (ej: "customerName", "phone", "babyAgeMonths", etc.)
    public string Value { get; set; } = string.Empty; // Valor del campo
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public virtual Conversation Conversation { get; set; } = null!;
}
