using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Persistencia columnar del estado del motor agentic (una fila por conversación).
/// </summary>
public class ConversationStateEntity
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public ConversationOwner Owner { get; set; } = ConversationOwner.Bot;
    public DateTime? LastEscalatedAt { get; set; }
    public int ConsecutiveDegradedTurns { get; set; }
    public string? LastUserMessage { get; set; }
    public string? LastBotMessage { get; set; }
    public string? PreviousSessionJson { get; set; }
    public string? VerificationsJson { get; set; }
    public DateTime SessionStartedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Conversation Conversation { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
}
