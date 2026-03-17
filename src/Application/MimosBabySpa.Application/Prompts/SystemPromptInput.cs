using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Input unificado para construir el system prompt.
/// Agrupa el contexto de negocio y la categoría del servicio seleccionado (cuando aplica).
/// </summary>
public record SystemPromptInput(
    LoadedBusinessContext BusinessContext,
    Guid? SelectedCategoryId = null);
