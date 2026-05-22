namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Estado del motor agentic (universal, multitenant).
/// Solo metadatos de orquestación — hechos libres en ConversationContexts, booking en Reservations, pago en PaymentTransactions.
/// </summary>
public class ConversationState
{
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public ConversationOwner Owner { get; set; } = ConversationOwner.Bot;
    public DateTime? LastEscalatedAt { get; set; }
    public int ConsecutiveDegradedTurns { get; set; }
    public string? LastUserMessage { get; set; }
    public string? LastBotMessage { get; set; }
    public Dictionary<string, VerificationEntry> Verifications { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ConversationOwner
{
    Bot,
    Human
}
