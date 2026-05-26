using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Condición declarativa de activación de un stage o de un execute.
/// Soporta referencias @fact.X, @pack.X, @result.X, @const.X.
/// </summary>
public sealed class AgentFlowStageCondition
{
    /// <summary>Referencia que se evalúa, p. ej. "@pack.booking.has_active_reservation" o "@result.flow".</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>Valor esperado (comparación case-insensitive). Serializado como "equals" en JSON.</summary>
    [JsonPropertyName("equals")]
    public string EqualsValue { get; init; } = string.Empty;
}
