using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Interface para proveedores de prompts.
/// Permite construir prompts de forma modular y testeable.
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Construye el prompt usando el contexto de negocio.
    /// </summary>
    Task<string> BuildAsync(
        LoadedBusinessContext context,
        CancellationToken cancellationToken = default);
}
