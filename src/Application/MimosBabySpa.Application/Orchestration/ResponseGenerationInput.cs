using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Datos necesarios para construir el prompt de generación de respuesta (FASE 5).
///
/// Desacopla el orquestador del método de generación de respuesta.
/// Solo transferencia de datos — sin lógica.
///
/// BusinessContext proporciona acceso a RequiredFieldsConfiguration y a las
/// AttributeDefinitions del tenant, necesarios para el formulario de confirmación.
/// </summary>
public class ResponseGenerationInput
{
    public ConversationState State              { get; init; } = null!;
    public FlowEvaluationResult FlowSnapshot    { get; init; } = null!;
    public TurnActions TurnActions              { get; init; } = new();
    public ExtractionOutput ExtractionOutput    { get; init; } = new();
    public LoadedBusinessContext BusinessContext { get; init; } = null!;
    public string UserMessage                   { get; init; } = string.Empty;
    public string SystemPrompt                  { get; init; } = string.Empty;
    public Guid ConversationId                  { get; init; }
    public Guid BusinessId                      { get; init; }

    /// <summary>
    /// Indica si es el primer mensaje del usuario (historial vacío).
    /// Se asigna tras cargar el historial; usada para incluir instrucción de presentación.
    /// </summary>
    public bool IsFirstMessage                  { get; set; }
}
