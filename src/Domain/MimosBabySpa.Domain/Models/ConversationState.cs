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

    /// <summary>
    /// Snapshots de los facts en el momento en que cada etapa fue completada por primera vez.
    /// Clave: stageId · Valor: factKey → factValue al momento de completarse.
    /// Usado para detectar si el cliente cambió datos relevantes después del cierre de una etapa.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> StageFactSnapshots { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Ids de etapas con CompletesOnEnter=true que ya se ejecutaron al menos una vez.
    /// Evita que el saludo (u otras etapas de un solo disparo) se repitan.
    /// </summary>
    public HashSet<string> CompletedOneShotStages { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Ids de etapas con ExecutesTool que ya ejecutaron su tool con éxito.
    /// Permite al detector de etapas avanzar de checkout → closure sin esperar facts adicionales.
    /// Se borra si reentryOnFactChanged detecta un cambio relevante en la etapa.
    /// </summary>
    public HashSet<string> CompletedActionStages { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Fact key preguntado en el turno anterior del bot. Usado por NEXT MOVE para interpretar respuestas.
    /// </summary>
    public string? LastAskedFact { get; set; }

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ConversationOwner
{
    Bot,
    Human
}
