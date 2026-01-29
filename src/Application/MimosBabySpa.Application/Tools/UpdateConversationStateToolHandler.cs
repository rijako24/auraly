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

    public override string FunctionName => "update_conversation_state";

    public UpdateConversationStateToolHandler(
        IConversationStateManager stateManager,
        ILogger<UpdateConversationStateToolHandler> logger,
        IFlowEngine flowEngine)
        : base(stateManager, logger)
    {
        _flowEngine = flowEngine;
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

            // Validar que el valor no sea una frase (heurística simple)
            if (IsPhrase(value))
            {
                _logger.LogWarning("Valor rechazado por ser una frase: {Value}", value);
                return Task.FromResult(new ToolExecutionResult
                {
                    Success = false,
                    Message = $"Error: el valor '{value}' parece ser una frase. Solo se aceptan valores estructurados"
                });
            }

            // Actualizar el estado según el campo
            var result = UpdateStateField(context.State, field, value);

            if (!result.Success)
            {
                return Task.FromResult(result);
            }

            // Evaluar el estado después de la actualización
            var evaluation = _flowEngine.Evaluate(context.State, context.RequiredFields);

            // Construir mensaje de respuesta
            var responseMessage = $"✓ Campo '{field}' actualizado a '{value}'";
            
            if (evaluation.MissingFields.Any())
            {
                responseMessage += $". Aún faltan: {string.Join(", ", evaluation.MissingFields)}";
            }
            else
            {
                responseMessage += ". Todos los campos requeridos están completos";
            }

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

    private ToolExecutionResult UpdateStateField(
        Domain.Models.ConversationState state,
        string field,
        string value)
    {
        try
        {
            // Detectar si es un atributo de negocio
            if (field.StartsWith("Attribute:", StringComparison.OrdinalIgnoreCase))
            {
                var attributeName = field.Substring("Attribute:".Length);
                state.SetAttribute(attributeName, value);
                return new ToolExecutionResult
                {
                    Success = true,
                    Message = $"Atributo '{attributeName}' actualizado"
                };
            }

            // Actualizar campos core según el nombre
            switch (field)
            {
                case "CustomerName":
                    state.CustomerName = value;
                    break;

                case "Phone":
                    // Phone ya está en el estado desde la creación, no necesita actualizarse
                    // Pero lo aceptamos para no generar error
                    _logger.LogDebug("Campo 'Phone' ya está establecido en el estado, ignorando actualización");
                    break;

                case "Email":
                    if (!IsValidEmail(value))
                    {
                        return new ToolExecutionResult
                        {
                            Success = false,
                            Message = $"Error: '{value}' no es un email válido"
                        };
                    }
                    state.Email = value;
                    break;

                case "Service":
                    state.Service = value;
                    // Reset availability si cambia el servicio
                    state.AvailabilityConfirmed = false;
                    break;

                case "DesiredDate":
                    if (!DateOnly.TryParse(value, out var date))
                    {
                        return new ToolExecutionResult
                        {
                            Success = false,
                            Message = $"Error: '{value}' no es una fecha válida (formato: YYYY-MM-DD)"
                        };
                    }
                    if (state.DesiredDate.HasValue && state.DesiredDate.Value != date)
                    {
                        // Reset availability si cambia la fecha
                        state.AvailabilityConfirmed = false;
                    }
                    state.DesiredDate = date;
                    break;

                case "DesiredTime":
                    if (!TimeOnly.TryParse(value, out var time))
                    {
                        return new ToolExecutionResult
                        {
                            Success = false,
                            Message = $"Error: '{value}' no es una hora válida (formato: HH:MM)"
                        };
                    }
                    if (state.DesiredTime.HasValue && state.DesiredTime.Value != time)
                    {
                        // Reset availability si cambia la hora
                        state.AvailabilityConfirmed = false;
                    }
                    state.DesiredTime = time;
                    break;

                default:
                    return new ToolExecutionResult
                    {
                        Success = false,
                        Message = $"Error: campo '{field}' no reconocido. " +
                                 "Use 'Attribute:' como prefijo para atributos de negocio"
                    };
            }

            state.UpdatedAt = DateTime.UtcNow;
            state.Version++;

            return new ToolExecutionResult
            {
                Success = true,
                Message = $"Campo '{field}' actualizado exitosamente"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar campo {Field}", field);
            return new ToolExecutionResult
            {
                Success = false,
                Message = $"Error al actualizar campo: {ex.Message}",
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Valida si un valor parece ser una frase en lugar de un valor estructurado
    /// </summary>
    private bool IsPhrase(string value)
    {
        // REGLA: Los nombres de servicios son TÍTULOS PROPIOS y NO son frases
        // Ejemplos válidos: "Plan Suaves Mimos", "Masaje Relajante Premium", "Spa Bebé VIP"
        // Ejemplos de frases: "tengo un bebé de 5 meses", "quiero reservar para mañana"
        
        var valueLower = value.ToLower();
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Si tiene más de 8 palabras, probablemente es una frase
        if (words.Length > 8)
        {
            return true;
        }

        // Buscar patrones de frase (palabras completas, no substrings)
        var phrasePatterns = new[] { 
            "tengo", "tiene", "está", "son", "están",
            "me llamo", "mi bebé", "el bebé", 
            "quiero", "deseo", "necesito",
            "muy", "mucho", "poco", "algo", "nada"
        };
        
        foreach (var pattern in phrasePatterns)
        {
            // Buscar el patrón como palabra independiente
            if (System.Text.RegularExpressions.Regex.IsMatch(valueLower, $@"\b{pattern}\b"))
            {
                return true;
            }
        }

        // Buscar artículos seguidos de sustantivos comunes (patrón de frase)
        if (System.Text.RegularExpressions.Regex.IsMatch(valueLower, @"\b(el|la|los|las)\s+(bebé|niño|niña|hijo|hija)\b"))
        {
            return true;
        }

        // Si pasa las validaciones, es un valor estructurado (nombre, título, etc.)
        return false;
    }

    /// <summary>
    /// Valida si un string es un email válido
    /// </summary>
    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
