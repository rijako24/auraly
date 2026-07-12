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
    public DateTime? ActiveRequestStartedAtUtc { get; set; }
    public Dictionary<string, VerificationEntry> Verifications { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Durable deterministic cursor. It changes only after a compiled route or transition.</summary>
    public string? ActiveFlowId { get; set; }
    public string? ActiveStageId { get; set; }
    public DateTime? ActiveFlowExpiresAtUtc { get; set; }

    /// <summary>Monotonic versions used to invalidate dependent facts and verifications.</summary>
    public Dictionary<string, long> FactVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Validated semantic plan deferred until the customer resolves an ambiguity.</summary>
    public PendingTurnPlan? PendingTurnPlan { get; set; }
    public long RequestGeneration { get; set; }
    public long LastOpenedRequestGeneration { get; set; } = -1;
    public Dictionary<string, DateTime> ExecutedOperationKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Snapshots de los facts en el momento en que cada etapa fue completada por primera vez.
    /// Clave: stageId · Valor: factKey → factValue al momento de completarse.
    /// Usado para detectar si el cliente cambió datos relevantes después del cierre de una etapa.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> StageFactSnapshots { get; set; } = new(StringComparer.Ordinal);

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PendingTurnPlan
{
    public int SchemaVersion { get; set; } = 1;
    public string ConfigurationSignature { get; set; } = string.Empty;
    public string FlowId { get; set; } = string.Empty;
    public string StageId { get; set; } = string.Empty;
    public string PlanJson { get; set; } = string.Empty;
    public IReadOnlyList<string> AmbiguousFields { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public enum ConversationOwner
{
    Bot,
    Human
}
