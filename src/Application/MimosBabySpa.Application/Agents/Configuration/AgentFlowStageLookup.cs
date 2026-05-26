namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Tool de solo lectura ejecutada por el motor cada turno mientras el stage está activo.
/// Idempotente: no crea ni modifica datos. Enriquece el contexto del LLM.
/// </summary>
public sealed class AgentFlowStageLookup
{
    /// <summary>Nombre registrado de la tool (p. ej. "get_service_catalog").</summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>
    /// Argumentos resolvibles: valores literales o referencias @fact.X / @const.X.
    /// Ejemplo: { "service": "@fact.service" }.
    /// </summary>
    public IReadOnlyDictionary<string, string> Args { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
