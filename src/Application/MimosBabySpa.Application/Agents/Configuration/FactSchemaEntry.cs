namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class FactSchemaEntry
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Etiqueta legible para el LLM (ej. "edad del bebé").</summary>
    public string Label { get; init; } = string.Empty;

    public string Type { get; init; } = "string";

    public bool Required { get; init; }

    /// <summary>user | channel | system</summary>
    public string Source { get; init; } = "user";

    public bool PersistsAcrossConversations { get; init; }

    /// <summary>
    /// eager  → el LLM debe capturar este dato en cuanto el cliente lo mencione, sin esperar su etapa.
    /// onDemand → se captura cuando el flujo llega a la etapa correspondiente (comportamiento por defecto).
    /// </summary>
    public string CaptureMode { get; init; } = "onDemand";
}
