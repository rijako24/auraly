using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.Prompts.Extraction;
using MimosBabySpa.Domain.Models;
using System.Text;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Constructor modular de prompts para extracción estructurada con JSON Mode.
/// ✅ Refactorizado: Usa componentes reutilizables en lugar de prompt monolítico.
/// </summary>
public class JsonSchemaPromptBuilder
{
    private readonly CoreInstructionsBuilder _coreInstructions;
    private readonly StateContextBuilder _stateContext;
    private readonly FieldDefinitionsBuilder _fieldDefinitions;

    public JsonSchemaPromptBuilder()
    {
        _coreInstructions = new CoreInstructionsBuilder();
        _stateContext = new StateContextBuilder();
        _fieldDefinitions = new FieldDefinitionsBuilder();
    }

    /// <summary>
    /// Construye el prompt de extracción usando componentes modulares.
    /// ✅ NO hace queries a BD - usa datos del contexto precargado.
    /// </summary>
    public Task<string> BuildExtractionPromptAsync(
        LoadedBusinessContext businessContext,
        string userMessage,
        ConversationState currentState,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // 1. Instrucciones principales + contexto
        sb.AppendLine(_coreInstructions.Build(businessContext, userMessage));
        sb.AppendLine();

        // 2. Estado actual de la conversación
        sb.AppendLine(_stateContext.Build(currentState));
        sb.AppendLine();

        // 3. Campos disponibles (core + atributos)
        sb.AppendLine(_fieldDefinitions.Build(businessContext));
        sb.AppendLine();

        // 4. Reglas de confidence (desde ExtractionPrompts centralizados)
        sb.AppendLine(ExtractionPrompts.ConfidenceRules);
        sb.AppendLine();

        // 5. Detección de ambigüedad
        sb.AppendLine(ExtractionPrompts.AmbiguityDetection);
        sb.AppendLine();

        // 6. Análisis de flujo conversacional
        sb.AppendLine(ExtractionPrompts.FlowAnalysisRules);
        sb.AppendLine();

        // 6b. Inferencia de referencias implícitas (NUEVO)
        sb.AppendLine(ExtractionPrompts.ImplicitReferenceInference);
        sb.AppendLine();

        // 7. Manejo de respuestas negativas
        sb.AppendLine(ExtractionPrompts.NegativeResponseHandling);
        sb.AppendLine();

        // 8. JSON Schema de salida
        sb.AppendLine(JsonSchemaDefinition.Schema);
        sb.AppendLine();

        // 9. Verificación final
        sb.AppendLine(ExtractionPrompts.FinalVerification);
        sb.AppendLine();

        // 10. Ejemplo de extracción de nombre
        sb.AppendLine(ExtractionPrompts.CustomerNameExample);

        return Task.FromResult(sb.ToString());
    }
}
