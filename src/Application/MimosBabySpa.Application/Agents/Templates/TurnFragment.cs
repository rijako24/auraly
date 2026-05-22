namespace MimosBabySpa.Application.Agents.Templates;

/// <summary>
/// Fragmento de mensaje pendiente de renderizar al finalizar el turno.
/// La plantilla vive en el prompt del agente; los datos los aporta la tool.
/// </summary>
public sealed record TurnFragment(
    string TemplateId,
    IReadOnlyDictionary<string, object?> Data,
    FragmentRenderMode Mode = FragmentRenderMode.Inline,
    FragmentPriority Priority = FragmentPriority.Optional);
