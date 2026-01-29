using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Handler para la herramienta check_availability.
/// 
/// Esta herramienta consulta al backend para verificar disponibilidad.
/// El LLM NUNCA puede decidir disponibilidad, solo el backend.
/// 
/// PRINCIPIOS:
/// - Solo acepta parámetros estructurados (service name, date ISO, time ISO)
/// - Interpreta ÚNICAMENTE la respuesta del backend (is_available)
/// - No hace inferencias ni promesas antes de consultar
/// - Es completamente domain-agnostic
/// </summary>
public class CheckAvailabilityToolHandler : BaseToolHandler
{
    private readonly IAvailabilityService _availabilityService;
    private readonly IFlowEngine _flowEngine;
    private readonly CachedBusinessContextProvider _businessContextProvider;

    public override string FunctionName => "check_availability";

    public CheckAvailabilityToolHandler(
        IConversationStateManager stateManager,
        ILogger<CheckAvailabilityToolHandler> logger,
        IAvailabilityService availabilityService,
        IFlowEngine flowEngine,
        CachedBusinessContextProvider businessContextProvider)
        : base(stateManager, logger)
    {
        _availabilityService = availabilityService;
        _flowEngine = flowEngine;
        _businessContextProvider = businessContextProvider;
    }

    public override FunctionDefinition GetDefinition()
    {
        return new FunctionDefinition
        {
            Name = FunctionName,
            Description = @"Verifica disponibilidad para un servicio en una fecha/hora específica consultando al backend.

REGLAS CRÍTICAS:
- NUNCA prometer disponibilidad antes de llamar esta función
- Solo interpretar is_available de la respuesta del backend como verdad absoluta
- El servicio DEBE ser el nombre EXACTO del catálogo (sin abreviaciones ni sinónimos)
- La fecha DEBE estar en formato ISO (YYYY-MM-DD)
- La hora es opcional pero recomendada (formato HH:MM)
- Esta función SOLO consulta, NO crea ni reserva nada

CUANDO USAR:
✓ Usuario pregunta ""¿Hay disponibilidad el sábado?""
✓ Usuario dice ""Me gustaría ir mañana a las 3pm""
✓ Después de que el usuario proporcione fecha/hora y ANTES de confirmar
❌ NUNCA antes de tener servicio y fecha
❌ NUNCA después de que ya se verificó (is_available ya es true)

RESPUESTA:
- Si is_available = true: ""Sí hay disponibilidad"" + sugerir confirmar
- Si is_available = false: ""No hay disponibilidad"" + sugerir alternativas
- NUNCA inventar horarios disponibles, solo reportar lo que el backend responda",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    service = new
                    {
                        type = "string",
                        description = "Nombre EXACTO del servicio del catálogo (no usar abreviaciones)"
                    },
                    date = new
                    {
                        type = "string",
                        description = "Fecha en formato ISO (YYYY-MM-DD)"
                    },
                    time = new
                    {
                        type = "string",
                        description = "Hora en formato HH:MM (24h). Opcional pero recomendado"
                    }
                },
                required = new[] { "service", "date" }
            })
        };
    }

    protected override async Task<ToolExecutionResult> ExecuteCoreAsync(
        Dictionary<string, object> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extraer argumentos o usar valores del estado
            string service;
            if (arguments.TryGetValue("service", out var serviceObj) && serviceObj != null)
            {
                service = serviceObj.ToString() ?? string.Empty;
            }
            else if (!string.IsNullOrEmpty(context.State.Service))
            {
                service = context.State.Service;
                _logger.LogDebug("Usando servicio del estado: {Service}", service);
            }
            else
            {
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: el parámetro 'service' es requerido y no está en el estado"
                };
            }

            string dateStr;
            if (arguments.TryGetValue("date", out var dateObj) && dateObj != null)
            {
                dateStr = dateObj.ToString() ?? string.Empty;
            }
            else if (context.State.DesiredDate.HasValue)
            {
                dateStr = context.State.DesiredDate.Value.ToString("yyyy-MM-dd");
                _logger.LogDebug("Usando fecha del estado: {Date}", dateStr);
            }
            else
            {
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: el parámetro 'date' es requerido y no está en el estado"
                };
            }

            string? timeStr = null;
            if (arguments.TryGetValue("time", out var timeObj) && timeObj != null)
            {
                timeStr = timeObj?.ToString();
            }
            else if (context.State.DesiredTime.HasValue)
            {
                timeStr = context.State.DesiredTime.Value.ToString("HH:mm");
                _logger.LogDebug("Usando hora del estado: {Time}", timeStr);
            }

            // Validar formato de fecha
            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = $"Error: '{dateStr}' no es una fecha válida (formato: YYYY-MM-DD)"
                };
            }

            // Validar formato de hora si está presente
            TimeSpan? time = null;
            if (!string.IsNullOrWhiteSpace(timeStr))
            {
                if (!TimeOnly.TryParse(timeStr, out var timeOnly))
                {
                    return new ToolExecutionResult
                    {
                        Success = false,
                        Message = $"Error: '{timeStr}' no es una hora válida (formato: HH:MM)"
                    };
                }
                time = timeOnly.ToTimeSpan();
            }

            // Verificar que se puede consultar disponibilidad
            if (!_flowEngine.CanCheckAvailability(context.State))
            {
                _logger.LogWarning("Intento de verificar disponibilidad cuando no se puede");
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = "No se puede verificar disponibilidad en este momento. " +
                             "Asegúrate de que el servicio y la fecha estén establecidos"
                };
            }

            _logger.LogInformation(
                "Verificando disponibilidad: Service={Service}, Date={Date}, Time={Time}",
                service, dateStr, timeStr ?? "not specified");

            // Consultar al backend (ÚNICA FUENTE DE VERDAD)
            var availability = await _availabilityService.CheckAvailabilityAsync(
                context.BusinessId,
                service,
                date.ToDateTime(TimeOnly.MinValue),
                time,
                context.State.DurationMinutes,
                cancellationToken);

            // Actualizar el estado con el resultado del backend
            context.State.AvailabilityConfirmed = availability.IsAvailable;
            context.State.UpdatedAt = DateTime.UtcNow;
            context.State.Version++;

            // Si no se proporcionó hora específica, generar slots de horarios sugeridos
            if (availability.IsAvailable && string.IsNullOrWhiteSpace(timeStr))
            {
                var suggestedSlots = await GenerateSuggestedTimeSlotsAsync(
                    context.BusinessId,
                    date,
                    context.State.DurationMinutes ?? 60,
                    cancellationToken);
                
                if (suggestedSlots.Any())
                {
                    context.State.AvailableTimeSlots = string.Join(",", suggestedSlots);
                    _logger.LogInformation(
                        "Horarios sugeridos generados: {Slots}",
                        context.State.AvailableTimeSlots);
                }
            }

            // Construir mensaje de respuesta
            var responseMessage = availability.IsAvailable
                ? $"✓ Disponibilidad confirmada para {service} el {dateStr}" +
                  (timeStr != null ? $" a las {timeStr}" : "") +
                  ". " + availability.Message
                : $"✗ No hay disponibilidad para {service} el {dateStr}" +
                  (timeStr != null ? $" a las {timeStr}" : "") +
                  ". " + availability.Message;

            _logger.LogInformation(
                "Disponibilidad verificada: IsAvailable={IsAvailable}",
                availability.IsAvailable);

            return new ToolExecutionResult
            {
                Success = true,
                Message = responseMessage,
                StateModified = true,
                Data = new Dictionary<string, object>
                {
                    { "is_available", availability.IsAvailable },
                    { "service", service },
                    { "date", dateStr },
                    { "time", timeStr ?? "any" },
                    { "backend_message", availability.Message ?? string.Empty },
                    { "suggested_slots", context.State.AvailableTimeSlots ?? string.Empty }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar disponibilidad");
            return new ToolExecutionResult
            {
                Success = false,
                Message = $"Error al verificar disponibilidad: {ex.Message}",
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Genera horarios sugeridos basados en el horario del negocio.
    /// Retorna slots cada 90 minutos dentro del horario de operación.
    /// </summary>
    private async Task<List<string>> GenerateSuggestedTimeSlotsAsync(
        Guid businessId,
        DateOnly date,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        try
        {
            // Obtener contexto del negocio
            var businessContext = await _businessContextProvider.GetOrLoadAsync(businessId, cancellationToken);
            
            if (businessContext?.Info?.Schedule == null || 
                !businessContext.Info.Schedule.Any())
            {
                _logger.LogWarning("No hay horario configurado para el negocio {BusinessId}", businessId);
                return new List<string>();
            }

            // Obtener el día de la semana en inglés lowercase
            var dayOfWeek = date.DayOfWeek.ToString().ToLower();
            
            if (!businessContext.Info.Schedule.TryGetValue(dayOfWeek, out var timeBlocks) || 
                !timeBlocks.Any())
            {
                _logger.LogInformation(
                    "El negocio está cerrado el {DayOfWeek} ({Date})",
                    dayOfWeek, date.ToString("yyyy-MM-dd"));
                return new List<string>();
            }

            var suggestedSlots = new List<string>();
            const int slotIntervalMinutes = 90; // Generar slots cada 90 minutos

            // Para cada bloque horario del día (ej: mañana y tarde)
            foreach (var block in timeBlocks.Where(b => b.IsValid()))
            {
                var currentTime = block.OpenTime;
                var closeTime = block.CloseTime;

                // Generar slots hasta que ya no quepan en el horario
                while (currentTime.Add(TimeSpan.FromMinutes(durationMinutes)) <= closeTime)
                {
                    suggestedSlots.Add(currentTime.ToString(@"hh\:mm"));
                    currentTime = currentTime.Add(TimeSpan.FromMinutes(slotIntervalMinutes));
                }
            }

            _logger.LogDebug(
                "Generados {Count} slots sugeridos para {Date} ({DayOfWeek})",
                suggestedSlots.Count, date.ToString("yyyy-MM-dd"), dayOfWeek);

            return suggestedSlots;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar horarios sugeridos");
            return new List<string>();
        }
    }
}
