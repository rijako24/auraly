using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Input unificado para construir el system prompt.
/// Agrupa el contexto de negocio y la categoría del servicio seleccionado (cuando aplica).
/// </summary>
public record SystemPromptInput(
    LoadedBusinessContext BusinessContext,
    ServiceCategory? SelectedServiceCategory = null);
