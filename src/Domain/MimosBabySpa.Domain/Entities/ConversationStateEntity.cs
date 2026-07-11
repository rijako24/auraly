using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Domain.Entities;

public class ConversationStateEntity
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public ConversationOwner Owner { get; set; } = ConversationOwner.Bot;
    public DateTime? LastEscalatedAt { get; set; }
    public int ConsecutiveDegradedTurns { get; set; }
    public string? LastUserMessage { get; set; }
    public string? LastBotMessage { get; set; }
    public DateTime? ActiveRequestStartedAtUtc { get; set; }
    public string? VerificationsJson { get; set; }
    public string? StageSnapshotsJson { get; set; }
    public string? RuntimeStateJson { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Conversation Conversation { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
}
