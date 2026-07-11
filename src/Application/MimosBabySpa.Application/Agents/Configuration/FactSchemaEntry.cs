using System.Text.Json.Serialization;

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

    /// <summary>True when this fact should be shown in reservation calendar/email collected info.</summary>
    [JsonPropertyName("showInCollectedInfo")]
    public bool ShowInCollectedInfo { get; init; }

    /// <summary>Valor por defecto usado por el hidratador cuando el fact no existe.</summary>
    public string? DefaultValue { get; init; }

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
    /// Fact keys that must remain unchanged for this fact to stay valid.
    /// When any dependency changes, the engine may clear this fact and re-enter affected stages.
    /// Applies to request/ephemeral facts; customer-scoped facts are preserved.
    /// </summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Optional authority that validates/canonicalizes user-provided values.
    /// Example: catalog-backed facts are resolved by tools/catalog services, not aliases.
    /// </summary>
    public string? ValueSource { get; init; }

    /// <summary>
    /// Alias names that can be used to normalize set_fact keys for non-catalog facts.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
