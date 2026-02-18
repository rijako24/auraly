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
            Description = @"Actualiza el estado de la conversación con información estructurada extraída del mensaje del usuario.

REGLAS CRÍTICAS:
- Solo guardar valores ESTRUCTURADOS (nombres, fechas ISO, horas, emails validados)
- NUNCA guardar frases del usuario directamente (""tengo un bebé de 6 meses"" ❌, ""6"" ✓)
- NUNCA inventar o inferir información que el usuario no dio explícitamente
- NUNCA sobrescribir un valor válido existente a menos que sea una corrección explícita
- Para atributos de negocio, usar el nombre exacto del campo configurado

CAMPOS CORE (usar 'field' parameter):
- CustomerName: nombre del cliente
- Email: email validado
- Service: nombre EXACTO del servicio del catálogo
- DesiredDate: fecha en formato YYYY-MM-DD
- DesiredTime: hora en formato HH:MM (24h)

ATRIBUTOS DE NEGOCIO (usar 'field' con prefijo 'Attribute:'):
- Attribute:BabyAge: edad en meses (solo número)
- Attribute:BabyName: nombre del bebé
- Attribute:SpecialConditions: condiciones especiales
- Etc. (según configuración del negocio)

EJEMPLOS:
✓ Usuario: ""Mi bebé tiene 6 meses"" → field=""Attribute:BabyAge"", value=""6""
✓ Usuario: ""Se llama Lucas"" → field=""Attribute:BabyName"", value=""Lucas""
✓ Usuario: ""Me gustaría el masaje relajante"" → field=""Service"", value=""Masaje Relajante""
✓ Usuario: ""Para mañana a las 3pm"" → field=""DesiredDate"", value=""2026-01-27"" (+ DesiredTime=""15:00"")
❌ Usuario: ""Tengo un bebé pequeño"" → NO llamar la función (no hay valor estructurado)
❌ field=""Attribute:BabyAge"", value=""6 meses"" → ❌ Debe ser solo ""6""
❌ field=""DesiredDate"", value=""mañana"" → ❌ Debe ser ""2026-01-27""",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    field = new
                    {
                        type = "string",
                        description = "Nombre del campo a actualizar. Usar 'Attribute:' como prefijo para atributos de negocio"
                    },
                    value = new
                    {
                        type = "string",
                        description = "Valor ESTRUCTURADO a guardar (solo datos, nunca frases)"
                    }
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
