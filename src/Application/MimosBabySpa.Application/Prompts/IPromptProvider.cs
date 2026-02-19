namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Interface para proveedores de prompts.
/// Permite construir prompts de forma modular y testeable.
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Construye el prompt usando el input unificado.
    /// SelectedServiceCategory: cuando el cliente ya eligió servicio, filtra add-ons por categoría compatible.
    /// </summary>
    Task<string> BuildAsync(
        SystemPromptInput input,
        CancellationToken cancellationToken = default);
}
