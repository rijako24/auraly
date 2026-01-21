using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ToolDispatcher : IToolDispatcher
{
    private readonly ICalendarService _calendarService;
    private readonly IReservationService _reservationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ToolDispatcher> _logger;
    private static readonly List<ToolDefinition> _availableTools = new();

    static ToolDispatcher()
    {
        InitializeTools();
    }

    public ToolDispatcher(
        ICalendarService calendarService,
        IReservationService reservationService,
        IUnitOfWork unitOfWork,
        ILogger<ToolDispatcher> logger)
    {
        _calendarService = calendarService;
        _reservationService = reservationService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public List<ToolDefinition> GetAvailableTools()
    {
        return _availableTools;
    }

    public async Task<ToolCallResult> ExecuteToolAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Ejecutando tool: {ToolName} con ID: {ToolCallId}", toolCall.Name, toolCall.Id);

            return toolCall.Name switch
            {
                "check_availability" => await ExecuteCheckAvailabilityAsync(businessId, toolCall, cancellationToken),
                "create_reservation" => await ExecuteCreateReservationAsync(businessId, toolCall, cancellationToken),
                "update_conversation_state" => await ExecuteUpdateConversationStateAsync(businessId, toolCall, cancellationToken),
                _ => new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Herramienta desconocida: {toolCall.Name}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando tool {ToolName}", toolCall.Name);
            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = true,
                ErrorMessage = $"Error ejecutando herramienta: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCheckAvailabilityAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        CancellationToken cancellationToken)
    {
        try
        {
            if (toolCall.Arguments == null || !toolCall.Arguments.HasValue)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Argumentos no proporcionados"
                };
            }

            var args = toolCall.Arguments.Value;
            
            // Extraer parámetros requeridos: service, date
            if (!args.TryGetProperty("service", out var serviceProp) || 
                !args.TryGetProperty("date", out var dateProp))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Parámetros requeridos faltantes: service, date"
                };
            }

            var service = serviceProp.GetString() ?? string.Empty;
            var dateStr = dateProp.GetString() ?? string.Empty;

            if (!DateTime.TryParse(dateStr, out var date))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Fecha inválida: {dateStr}"
                };
            }

            // La hora y duración son opcionales para verificar disponibilidad
            // Si se proporciona time, también debe proporcionar durationMinutes
            TimeSpan? timeToCheck = null;
            int? durationMinutes = null;
            
            if (args.TryGetProperty("time", out var timeProp))
            {
                var timeStr = timeProp.GetString();
                if (!string.IsNullOrWhiteSpace(timeStr) && TimeSpan.TryParse(timeStr, out var parsedTime))
                {
                    timeToCheck = parsedTime;
                    
                    // Si se proporciona time, durationMinutes es requerido
                    if (!args.TryGetProperty("durationMinutes", out var durationProp) || 
                        durationProp.ValueKind != JsonValueKind.Number)
                    {
                        return new ToolCallResult
                        {
                            ToolCallId = toolCall.Id,
                            ToolName = toolCall.Name,
                            IsError = true,
                            ErrorMessage = "Cuando se proporciona 'time', también se requiere 'durationMinutes'"
                        };
                    }
                    durationMinutes = durationProp.GetInt32();
                }
            }

            // Si se proporciona hora específica, verificar solo ese horario
            if (timeToCheck.HasValue && durationMinutes.HasValue)
            {
                var hasConflict = await _unitOfWork.Reservations.ExistsOverlappingReservationAsync(
                    businessId,
                    date.Date,
                    timeToCheck.Value,
                    durationMinutes.Value);

                var result = new
                {
                    service = service,
                    date = date.ToString("yyyy-MM-dd"),
                    time = timeToCheck.Value.ToString(@"hh\:mm"),
                    durationMinutes = durationMinutes.Value,
                    available = !hasConflict
                };

                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    Content = JsonSerializer.Serialize(result),
                    IsError = false
                };
            }
            else
            {
                // Si no se proporciona hora, consultar todas las reservas del día
                var startOfDay = date.Date;
                var endOfDay = startOfDay.AddDays(1);

                var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
                    businessId,
                    startOfDay,
                    endOfDay);

                var bookedSlots = reservations.Select(r => new
                {
                    time = r.ReservationTime.ToString(@"hh\:mm"),
                    duration = r.DurationMinutes,
                    service = r.ServiceName
                }).ToList();

                var result = new
                {
                    service = service,
                    date = date.ToString("yyyy-MM-dd"),
                    bookedSlots = bookedSlots,
                    totalBookedSlots = bookedSlots.Count,
                    message = bookedSlots.Count == 0 
                        ? "No hay reservas para esta fecha. Todos los horarios de atención están disponibles según los horarios del negocio."
                        : $"Hay {bookedSlots.Count} reserva(s) confirmada(s) para esta fecha. Los demás horarios dentro del horario de atención del negocio están disponibles."
                };

                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    Content = JsonSerializer.Serialize(result),
                    IsError = false
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en check_availability");
            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCreateReservationAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        CancellationToken cancellationToken)
    {
        try
        {
            if (toolCall.Arguments == null || !toolCall.Arguments.HasValue)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Argumentos no proporcionados"
                };
            }

            var args = toolCall.Arguments.Value;

            // Validar y extraer parámetros requeridos (solo los genéricos)
            var requiredParams = new[] { "service", "date", "time", "durationMinutes" };
            var missingParams = requiredParams.Where(p => !args.TryGetProperty(p, out _)).ToList();
            
            if (missingParams.Any())
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Parámetros requeridos faltantes: {string.Join(", ", missingParams)}"
                };
            }

            var service = args.GetProperty("service").GetString() ?? string.Empty;
            var dateStr = args.GetProperty("date").GetString() ?? string.Empty;
            var timeStr = args.GetProperty("time").GetString() ?? string.Empty;

            // Validar formato de fecha
            if (!DateTime.TryParse(dateStr, out var date))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Fecha inválida: {dateStr}"
                };
            }

            // Validar formato de hora (HH:mm)
            if (!TimeSpan.TryParse(timeStr, out var time))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Hora inválida: {timeStr}. Formato esperado: HH:mm"
                };
            }

            // La duración debe venir en los parámetros de la tool (la IA la sabe desde BusinessInformation en el prompt)
            int durationMinutes;
            if (!args.TryGetProperty("durationMinutes", out var durationProp) || 
                durationProp.ValueKind != JsonValueKind.Number)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "durationMinutes es requerido y debe ser un número"
                };
            }
            durationMinutes = durationProp.GetInt32();

            // Extraer información adicional opcional para Notes
            // La IA debe enviar toda la información adicional en el campo "notes" como string
            string? notes = null;
            if (args.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind == JsonValueKind.String)
            {
                var notesValue = notesProp.GetString();
                if (!string.IsNullOrWhiteSpace(notesValue))
                {
                    notes = notesValue;
                }
            }

            var startDateTime = date.Date.Add(time);
            var endDateTime = startDateTime.AddMinutes(durationMinutes);

            // Verificar también en base de datos
            var hasConflict = await _unitOfWork.Reservations.ExistsOverlappingReservationAsync(
                businessId,
                date.Date,
                time,
                durationMinutes);

            if (hasConflict)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"El horario {dateStr} {timeStr} no está disponible (conflicto en base de datos)"
                };
            }

            // Crear la reserva con solo los campos genéricos
            // CustomerName y PhoneNumber se dejan vacíos o se pueden obtener de Notes si es necesario
            var reservation = new Reservation
            {
                ReservationId = Guid.NewGuid(),
                BusinessId = businessId,
                CustomerName = string.Empty, // Genérico - no aplica a todos los negocios
                PhoneNumber = string.Empty, // Genérico - no aplica a todos los negocios
                ServiceName = service,
                ReservationDate = date.Date,
                ReservationTime = time,
                DurationMinutes = durationMinutes,
                Notes = notes, // Cualquier información adicional específica del negocio va aquí
                Status = Domain.Enums.ReservationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            var reservationDto = await _reservationService.CreateReservationAsync(reservation, cancellationToken);

            var result = new
            {
                success = true,
                reservationId = reservationDto.ReservationId.ToString(),
                service = reservationDto.ServiceName,
                date = reservationDto.ReservationDate.ToString("yyyy-MM-dd"),
                time = reservationDto.ReservationTime.ToString(@"hh\:mm"),
                durationMinutes = reservationDto.DurationMinutes,
                status = reservationDto.Status.ToString()
            };

            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Content = JsonSerializer.Serialize(result),
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en create_reservation");
            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<ToolCallResult> ExecuteUpdateConversationStateAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        CancellationToken cancellationToken)
    {
        try
        {
            if (toolCall.Arguments == null || !toolCall.Arguments.HasValue)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Argumentos no proporcionados"
                };
            }

            var args = toolCall.Arguments.Value;

            // Validar parámetros requeridos
            if (!args.TryGetProperty("conversationId", out var conversationIdProp) ||
                !args.TryGetProperty("field", out var fieldProp) ||
                !args.TryGetProperty("value", out var valueProp))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Parámetros requeridos faltantes: conversationId, field, value"
                };
            }

            if (!Guid.TryParse(conversationIdProp.GetString(), out var conversationId))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"conversationId inválido: {conversationIdProp.GetString()}"
                };
            }

            var field = fieldProp.GetString() ?? string.Empty;
            var value = valueProp.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(field))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "El campo 'field' no puede estar vacío"
                };
            }

            // Verificar que la conversación pertenece al negocio
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation == null)
            {
                _logger.LogWarning("Conversación {ConversationId} no encontrada en la base de datos", conversationId);
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Conversación {conversationId} no encontrada"
                };
            }
            
            if (conversation.BusinessId != businessId)
            {
                _logger.LogWarning("Conversación {ConversationId} pertenece al negocio {ConversationBusinessId} pero se esperaba {BusinessId}", 
                    conversationId, conversation.BusinessId, businessId);
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"Conversación {conversationId} no pertenece al negocio {businessId}"
                };
            }

            // Crear o actualizar el contexto
            await _unitOfWork.ConversationContexts.CreateOrUpdateAsync(conversationId, field, value);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var result = new
            {
                success = true,
                conversationId = conversationId.ToString(),
                field = field,
                value = value,
                message = $"Contexto actualizado: {field} = {value}"
            };

            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Content = JsonSerializer.Serialize(result),
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en update_conversation_state");
            return new ToolCallResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static void InitializeTools()
    {
        // Tool 1: check_availability
        var checkAvailabilitySchema = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""service"": {
                    ""type"": ""string"",
                    ""description"": ""Nombre del servicio o plan que se desea consultar""
                },
                ""date"": {
                    ""type"": ""string"",
                    ""format"": ""date"",
                    ""description"": ""Fecha para la cual se desea verificar disponibilidad (formato: YYYY-MM-DD)""
                },
                ""time"": {
                    ""type"": ""string"",
                    ""pattern"": ""^\\d{2}:\\d{2}$"",
                    ""description"": ""Hora específica a verificar (opcional, formato: HH:mm). Si se proporciona, también se requiere durationMinutes. Si no se proporciona, devuelve todas las reservas del día""
                },
                ""durationMinutes"": {
                    ""type"": ""integer"",
                    ""description"": ""Duración del servicio en minutos (requerido si se proporciona 'time', debe obtenerse de BusinessInformation en el prompt)""
                }
            },
            ""required"": [""service"", ""date""]
        }");

        _availableTools.Add(new ToolDefinition
        {
            Name = "check_availability",
            Description = "Verifica disponibilidad de horarios para un servicio y una fecha determinada. Si se proporciona 'time', también se debe proporcionar 'durationMinutes' para verificar ese horario específico (devuelve 'available: true/false'). Si NO se proporciona 'time', devuelve todas las reservas confirmadas del día en 'bookedSlots' y un mensaje explicativo. IMPORTANTE: Los 'bookedSlots' son SOLO las reservas confirmadas del día. Los demás horarios dentro del horario de atención del negocio están DISPONIBLES. Una reserva en un horario específico NO significa que todo el día esté ocupado. Siempre consulta los horarios de atención del negocio en la información proporcionada para determinar qué horarios están disponibles.",
            ParametersSchema = checkAvailabilitySchema
        });

        // Tool 2: create_reservation
        var createReservationSchema = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""service"": {
                    ""type"": ""string"",
                    ""description"": ""Nombre del servicio o plan a reservar""
                },
                ""date"": {
                    ""type"": ""string"",
                    ""format"": ""date"",
                    ""description"": ""Fecha de la reserva (formato: YYYY-MM-DD)""
                },
                ""time"": {
                    ""type"": ""string"",
                    ""pattern"": ""^\\d{2}:\\d{2}$"",
                    ""description"": ""Hora de la reserva (formato: HH:mm, ejemplo: 14:30)""
                },
                ""durationMinutes"": {
                    ""type"": ""integer"",
                    ""description"": ""Duración del servicio en minutos (debe obtenerse de BusinessInformation en el prompt)""
                },
                ""notes"": {
                    ""type"": ""string"",
                    ""description"": ""Información adicional opcional sobre la reserva. Formato: JSON string con pares clave-valor. Ejemplo: '{\""customerName\"":\""Juan Pérez\"",\""phone\"":\""+1234567890\"",\""age\"":\""6 meses\""}'. Incluye cualquier información relevante para el tipo de negocio (nombre del cliente, teléfono, edad, preferencias, etc.) en formato JSON string.""
                }
            },
            ""required"": [""service"", ""date"", ""time"", ""durationMinutes""]
        }");

        _availableTools.Add(new ToolDefinition
        {
            Name = "create_reservation",
            Description = "Crea una reserva en el sistema y genera un evento real en el calendario del negocio. Solo requiere campos genéricos: servicio, fecha, hora y duración. Para información adicional específica del negocio (nombre del cliente, teléfono, edad, preferencias, etc.), envía todo en el campo opcional 'notes' como un JSON string con formato: '{\"clave\":\"valor\",\"otraClave\":\"otroValor\"}'. Ejemplo: '{\"customerName\":\"Juan Pérez\",\"phone\":\"+1234567890\",\"age\":\"6 meses\"}'.",
            ParametersSchema = createReservationSchema
        });

        // Tool 3: update_conversation_state
        var updateConversationStateSchema = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""conversationId"": {
                    ""type"": ""string"",
                    ""format"": ""uuid"",
                    ""description"": ""ID de la conversación (UUID)""
                },
                ""field"": {
                    ""type"": ""string"",
                    ""description"": ""Nombre del campo de contexto. Campos disponibles: customerName (nombre del cliente), phone (teléfono), babyAgeMonths (edad del bebé en meses - MUY IMPORTANTE), service (servicio o plan elegido), desiredDate (fecha deseada), desiredTime (hora deseada), reservationConfirmed (confirmación de reserva)""
                },
                ""value"": {
                    ""type"": ""string"",
                    ""description"": ""Valor del campo de contexto""
                }
            },
            ""required"": [""conversationId"", ""field"", ""value""]
        }");

        _availableTools.Add(new ToolDefinition
        {
            Name = "update_conversation_state",
            Description = "OBLIGATORIO: Guarda información importante del cliente en el contexto de la conversación. DEBES usar esta herramienta INMEDIATAMENTE cuando el cliente mencione: (1) Su nombre → field='customerName', (2) Su teléfono → field='phone', (3) La edad del bebé (ej: 'tiene 4 meses', 'mi bebé tiene 6 meses', 'tiene 1 año') → field='babyAgeMonths' (convierte años a meses: 1 año = 12 meses), (4) Un servicio o plan → field='service', (5) Una fecha deseada → field='desiredDate', (6) Una hora deseada → field='desiredTime', (7) Confirmación explícita de reserva → field='reservationConfirmed'. IMPORTANTE: Si el cliente dice 'mi bebé tiene X meses' o 'tiene X meses' o 'X meses', DEBES llamar esta herramienta con field='babyAgeMonths' y value='X' (solo el número) IMPORTANTE No inventar valores aunque sea requerido",
            ParametersSchema = updateConversationStateSchema
        });
    }
}
