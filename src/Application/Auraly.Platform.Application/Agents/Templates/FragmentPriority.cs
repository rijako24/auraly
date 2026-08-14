namespace Auraly.Platform.Application.Agents.Templates;

/// <summary>
/// Prioridad de inclusión de un fragmento Inline en la respuesta final.
/// </summary>
public enum FragmentPriority
{
    /// <summary>Solo se incluye si el LLM referencia el token.</summary>
    Optional,

    /// <summary>Se prepone automáticamente si el LLM omite el token.</summary>
    Required
}
