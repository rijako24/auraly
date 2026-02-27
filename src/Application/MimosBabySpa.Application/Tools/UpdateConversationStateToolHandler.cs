using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Handler para la herramienta update_conversation_state.
/// 
/// Esta es la herramienta MÁS IMPORTANTE del sistema.
/// Permite al LLM actualizar el estado de la conversación con valores estructurados.
/// 
/// PRINCIPIOS:
/// - Solo acepta valores estructurados (nunca frases o JSON blobs)
/// - No sobrescribe valores válidos a menos que sea una corrección explícita
/// - No infiere ni inventa valores
/// - Es completamente domain-agnostic
/// </summary>
public class UpdateConversationStateToolHandler : BaseToolHandler
{
    private readonly IFlowEngine _flowEngine;
    private readonly IConversationStateUpdater _updater;

    public override string FunctionName => "update_conversation_state";

    public UpdateConversationStateToolHandler(
        IConversationStateManager stateManager,
        ILogger<UpdateConversationStateToolHandler> logger,
        IFlowEngine flowEngine,
        IConversationStateUpdater updater)
        : base(stateManager, logger)
    {
        _flowEngine = flowEngine;
        _updater = updater;
    }

    public override FunctionDefinition GetDefinition()
    {
        return new FunctionDefinition
        {
            Name = FunctionName,
            Description = "Actualiza el estado de la conversación con campo y valor estructurados. Usado por la extracción.",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    field = new { type = "string" },
                    value = new { type = "string" }
                },
                required = new[] { "field", "value" }
            })
        };
    }

    protected override Task<ToolExecutionResult> ExecuteCoreAsync(
        Dictionary<string, object> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extraer argumentos
            if (!arguments.TryGetValue("field", out var fieldObj) || fieldObj == null)
            {
                return Task.FromResult(new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: el parámetro 'field' es requerido"
                });
            }

            if (!arguments.TryGetValue("value", out var valueObj) || valueObj == null)
            {
                return Task.FromResult(new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: el parámetro 'value' es requerido"
                });
            }

            var field = fieldObj.ToString() ?? string.Empty;
            var value = valueObj.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult(new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: 'field' y 'value' no pueden estar vacíos"
                });
            }

            _logger.LogInformation("Actualizando campo: {Field} = {Value}", field, value);

            var applyResult = _updater.ApplyField(context.State, field, value);
            if (!applyResult.Success)
                return Task.FromResult(new ToolExecutionResult { Success = false, Message = applyResult.Message });

            var evaluation = _flowEngine.Evaluate(context.State, context.RequiredFields);
            var responseMessage = $"✓ Campo '{field}' actualizado a '{value}'";
            if (evaluation.MissingFields.Any())
                responseMessage += $". Aún faltan: {string.Join(", ", evaluation.MissingFields)}";
            else
                responseMessage += ". Todos los campos requeridos están completos";

            return Task.FromResult(new ToolExecutionResult
            {
                Success = true,
                Message = responseMessage,
                StateModified = true,
                Data = new Dictionary<string, object>
                {
                    { "field", field },
                    { "value", value },
                    { "completeness", evaluation.CompletenessPercentage },
                    { "missing_fields", evaluation.MissingFields }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar estado de conversación");
            return Task.FromResult(new ToolExecutionResult
            {
                Success = false,
                Message = $"Error interno: {ex.Message}",
                Exception = ex
            });
        }
    }

}
