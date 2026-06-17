namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class FactSchemaEntry
{
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Rol semántico universal (ej. "customer.name", "booking.service").
    /// Permite que herramientas del motor busquen datos por intención, no por clave literal.
    /// Opcional; si omitido, solo se accede por Key.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>Etiqueta legible para el LLM (ej. "edad del bebé").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>string | number | date | time | phone | email</summary>
    public string Type { get; init; } = "string";

    public bool Required { get; init; }

    /// <summary>user | channel | system</summary>
    public string Source { get; init; } = "user";

    /// <summary>
    /// customer: stable customer/baby data kept across requests.
    /// request: current request data, cleared when the request completes.
    /// ephemeral: derived/verification-like data, recalculated frequently.
    /// If omitted, the engine infers request for user/channel facts and ephemeral for system/session facts.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Optional retention window for this fact. When elapsed, the fact is ignored/cleared.
    /// </summary>
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Clears this fact when the business day changes, even if retention has not elapsed.
    /// </summary>
    public bool ExpireOnBusinessDayChange { get; init; }

    /// <summary>
    /// eager  → el LLM debe capturar este dato en cuanto el cliente lo mencione, sin esperar su etapa.
    /// onDemand → se captura cuando el flujo llega a la etapa correspondiente (comportamiento por defecto).
    /// </summary>
    public string CaptureMode { get; init; } = "onDemand";

    /// <summary>
    /// Nombres alternativos que el LLM puede usar al llamar set_fact.
    /// Ej. ["nombre", "cliente"] para key=customer_name.
    /// El motor los normaliza a Key canónico antes de persistir.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
