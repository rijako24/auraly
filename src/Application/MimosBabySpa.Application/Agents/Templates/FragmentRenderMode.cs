namespace MimosBabySpa.Application.Agents.Templates;

/// <summary>
/// Política de composición de un fragmento de plantilla al finalizar el turno.
/// </summary>
public enum FragmentRenderMode
{
    /// <summary>Reemplaza el token en el texto del LLM (comportamiento por defecto).</summary>
    Inline,

    /// <summary>Descarta el texto del LLM y emite solo la plantilla renderizada.</summary>
    Exclusive
}
