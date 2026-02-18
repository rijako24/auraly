using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts.Extraction;
using MimosBabySpa.Domain.Models;
using System.Text;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Construye el prompt de extracción estructurada (JSON Mode).
///
/// Diseño lean: ~500 tokens vs ~2,000 del anterior.
/// - Reglas unificadas en un solo bloque (sin repetición).
/// - El mensaje del usuario se pasa como rol "user" en el request, no en el system prompt.
/// - El schema JSON es compacto (3 campos por field, no 7).
/// - Sin instrucciones específicas de negocio hardcodeadas.
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
    /// Construye el system prompt para extracción.
    /// El mensaje del usuario se envía aparte como rol "user" en el LLMRequest.
    /// </summary>
    public Task<string> BuildExtractionPromptAsync(
        LoadedBusinessContext businessContext,
        string userMessage,
        ConversationState currentState,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // 1. Instrucciones core + reglas unificadas
        sb.AppendLine(_coreInstructions.Build(businessContext));
        sb.AppendLine();

        // 2. Estado actual compacto (solo datos)
        sb.AppendLine(_stateContext.Build(currentState));
        sb.AppendLine();

        // 3. Campos disponibles (tabla dinámica por tenant)
        sb.AppendLine(_fieldDefinitions.Build(businessContext));
        sb.AppendLine();

        // 4. Schema de salida compacto
        sb.AppendLine(JsonSchemaDefinition.Schema);

        return Task.FromResult(sb.ToString());
    }
}
