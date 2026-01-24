using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ToolDispatcher : IToolDispatcher
{
    private readonly ICalendarService _calendarService;
    private readonly IReservationService _reservationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAvailabilityService _availabilityService;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly IConversationContextService _contextService;
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
        IAvailabilityService availabilityService,
        IEmployeeAssignmentService employeeAssignmentService,
        IConversationContextService contextService,
        ILogger<ToolDispatcher> logger)
    {
        _calendarService = calendarService;
        _reservationService = reservationService;
        _unitOfWork = unitOfWork;
        _availabilityService = availabilityService;
        _employeeAssignmentService = employeeAssignmentService;
        _contextService = contextService;
        _logger = logger;
    }

    public List<ToolDefinition> GetAvailableTools()
    {
        return _availableTools;
    }

    public async Task<ToolCallResult> ExecuteToolAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Ejecutando tool: {ToolName} con ID: {ToolCallId}, ConversationId: {ConversationId}",
                toolCall.Name, toolCall.Id, conversationId);

            return toolCall.Name switch
            {
                "check_availability" => await ExecuteCheckAvailabilityAsync(businessId, toolCall, cancellationToken),
                "create_reservation" => await ExecuteCreateReservationAsync(businessId, toolCall, conversationId, cancellationToken),
                "update_conversation_state" => await ExecuteUpdateConversationStateAsync(businessId, toolCall, conversationId, cancellationToken),
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

            // REFACTORIZADO: Usar AvailabilityService para cálculo determinístico
            // Toda la lógica de negocio (validación de empleados y recursos físicos) está encapsulada en AvailabilityService
            // AvailabilityService valida empleados ANTES de recursos físicos internamente
            var availabilityResult = await _availabilityService.CheckAvailabilityAsync(
                businessId,
                service,
                date,
                timeToCheck,
                durationMinutes,
                cancellationToken);

            _logger.LogInformation(
                "check_availability ejecutado: Service={Service}, Date={Date}, IsAvailable={IsAvailable}, CurrentReservations={CurrentReservations}",
                service, date.ToString("yyyy-MM-dd"), availabilityResult.IsAvailable, availabilityResult.CurrentReservations);

            // Construir resultado EXPLÍCITO que el modelo debe usar sin inferir
            var bookedSlotsData = availabilityResult.BookedSlots.Select(slot => new
            {
                time = slot.Time,
                endTime = slot.EndTime,
                duration = slot.Duration,
                service = slot.Service
            }).ToList();

            var overlappingSlotsData = availabilityResult.OverlappingSlots.Select(slot => new
            {
                time = slot.Time,
                endTime = slot.EndTime,
                duration = slot.Duration,
                service = slot.Service
            }).ToList();

            var result = new Dictionary<string, object>
            {
                { "service", service },
                { "date", date.ToString("yyyy-MM-dd") },
                { "is_available", availabilityResult.IsAvailable }, // VALOR EXPLÍCITO - el modelo NO debe inferir
                { "current_reservations", availabilityResult.CurrentReservations },
                { "bookedSlots", bookedSlotsData },
                { "totalBookedSlots", availabilityResult.CurrentReservations }
            };

            // Si se proporciona hora específica, agregar información adicional
            if (timeToCheck.HasValue && durationMinutes.HasValue)
            {
                var requestedTimeStr = $"{timeToCheck.Value.Hours:D2}:{timeToCheck.Value.Minutes:D2}";
                var requestedEndTime = timeToCheck.Value.Add(TimeSpan.FromMinutes(durationMinutes.Value));
                var requestedEndTimeStr = $"{requestedEndTime.Hours:D2}:{requestedEndTime.Minutes:D2}";
                
                result.Add("requestedTime", requestedTimeStr);
                result.Add("requestedDurationMinutes", durationMinutes.Value);
                result.Add("requestedEndTime", requestedEndTimeStr);
                result.Add("overlappingSlots", overlappingSlotsData);
            }
            
            result.Add("message", availabilityResult.Message);

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
        Guid? conversationId,
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

            // Completar parámetros faltantes desde el contexto de la conversación si está disponible
            var enrichedArgs = await EnrichReservationParametersFromContextAsync(args, conversationId, cancellationToken);
            args = enrichedArgs; // Usar los argumentos enriquecidos

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

            // REFACTORIZADO: Validar disponibilidad ATÓMICAMENTE antes de crear la reserva
            // Esto previene sobre-reservas y garantiza consistencia bajo concurrencia
            var reservationDateTime = date.Date.Add(time);
            
            _logger.LogInformation(
                "Validando disponibilidad antes de crear reserva: BusinessId={BusinessId}, Service={Service}, DateTime={DateTime}",
                businessId, service, reservationDateTime);

            // Verificar disponibilidad dentro de la transacción
            var availabilityCheck = await _availabilityService.CheckAvailabilityAsync(
                businessId,
                service,
                date,
                time,
                durationMinutes,
                cancellationToken);

            if (!availabilityCheck.IsAvailable)
            {
                _logger.LogWarning(
                    "Intento de crear reserva en horario no disponible: BusinessId={BusinessId}, Service={Service}, DateTime={DateTime}, OverlappingSlots={OverlappingCount}",
                    businessId, service, reservationDateTime, availabilityCheck.OverlappingSlots.Count);

                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"El horario {time:HH\\:mm} no está disponible. {availabilityCheck.Message}"
                };
            }

            // Obtener el ServiceId desde el nombre del servicio
            var serviceEntity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, service);
            if (serviceEntity == null)
            {
                _logger.LogWarning("Servicio '{Service}' no encontrado para negocio {BusinessId}", service, businessId);
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"El servicio '{service}' no existe o no está activo."
                };
            }

            // Asignar empleado automáticamente usando la lógica de prioridad por polivalencia
            var reservationEndTime = reservationDateTime.AddMinutes(durationMinutes);
            var assignedEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                businessId,
                serviceEntity.ServiceId,
                reservationDateTime,
                reservationEndTime,
                cancellationToken);

            if (assignedEmployee == null)
            {
                _logger.LogWarning(
                    "No hay personal disponible para servicio {Service} en horario {DateTime}",
                    service, reservationDateTime);
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = $"No hay personal disponible para este servicio en el horario {time:HH\\:mm}. Por favor, intenta con otro horario."
                };
            }

            _logger.LogInformation(
                "Empleado asignado automáticamente: {EmployeeId} ({EmployeeName}) para servicio {Service}",
                assignedEmployee.EmployeeId, assignedEmployee.Name, service);

            // Crear la reserva con ServiceId y EmployeeId asignados
            var reservation = new Reservation
            {
                ReservationId = Guid.NewGuid(),
                BusinessId = businessId,
                ServiceId = serviceEntity.ServiceId,
                Service = serviceEntity, // Asignar Service para que esté disponible
                EmployeeId = assignedEmployee.EmployeeId,
                Employee = assignedEmployee, // Asignar Employee para que esté disponible
                CustomerName = string.Empty, // Genérico - no aplica a todos los negocios
                PhoneNumber = string.Empty, // Genérico - no aplica a todos los negocios
                ReservationDateTime = reservationDateTime,
                DurationMinutes = durationMinutes,
                ConversationId = conversationId,
                Status = Domain.Enums.ReservationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Crear reserva - la validación de capacidad ya se hizo arriba
            var reservationDto = await _reservationService.CreateReservationAsync(reservation, cancellationToken);

            _logger.LogInformation(
                "Reserva creada exitosamente: ReservationId={ReservationId}, Service={Service}, DateTime={DateTime}",
                reservationDto.ReservationId, reservationDto.ServiceName, reservationDto.ReservationDateTime);

            // Guardar reserva confirmada en el estado de conversación
            if (conversationId.HasValue)
            {
                await _contextService.MarkReservationConfirmedAsync(
                    conversationId.Value, 
                    reservationDto.ReservationId.ToString());
            }

            var result = new
            {
                success = true, // SOLO true si la reserva fue creada exitosamente
                reservationId = reservationDto.ReservationId.ToString(),
                service = reservationDto.ServiceName,
                date = reservationDto.ReservationDateTime.ToString("yyyy-MM-dd"),
                time = reservationDto.ReservationDateTime.ToString(@"HH\:mm"),
                durationMinutes = reservationDto.DurationMinutes,
                status = reservationDto.Status.ToString(),
                message = "Reserva creada exitosamente. El modelo DEBE informar al usuario basándose en este resultado."
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
        Guid? conversationIdFromCaller,
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

            // conversationId siempre viene como parámetro del método
            if (!conversationIdFromCaller.HasValue)
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "conversationId es requerido y no se proporcionó en el contexto"
                };
            }

            var conversationId = conversationIdFromCaller.Value;

            // Validar parámetros requeridos restantes
            if (!args.TryGetProperty("field", out var fieldProp) ||
                !args.TryGetProperty("value", out var valueProp))
            {
                return new ToolCallResult
                {
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    IsError = true,
                    ErrorMessage = "Parámetros requeridos faltantes: field, value"
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

            // Usar método genérico que maneja el mapeo automáticamente
            await _contextService.SetFieldAsync(conversationId, field, value);

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
            Description = "Obtiene información DETERMINÍSTICA de disponibilidad desde el backend. Retorna 'is_available' (true/false) calculado por el sistema. El modelo NO debe inferir disponibilidad, solo usar el valor 'is_available' retornado. También retorna 'current_reservations', 'bookedSlots' y 'overlappingSlots' si se proporciona hora específica. IMPORTANTE: El modelo DEBE confiar en 'is_available' como verdad absoluta. NO debe aplicar reglas propias ni reinterpretar estos valores.",
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
                }
            },
            ""required"": [""service"", ""date"", ""time"", ""durationMinutes""]
        }");

        _availableTools.Add(new ToolDefinition
        {
            Name = "create_reservation",
            Description = "Crea una reserva en el sistema con validación ATÓMICA de capacidad. El backend valida disponibilidad antes de crear. Retorna 'success=true' solo si la reserva fue creada exitosamente. Si 'success=false', el modelo NO debe confirmar la reserva al usuario. Solo requiere campos genéricos: servicio, fecha, hora y duración. IMPORTANTE: El modelo solo debe confirmar reserva si recibe 'success=true'.",
            ParametersSchema = createReservationSchema
        });

        // Tool 3: update_conversation_state
        // conversationId se inyecta automáticamente desde el contexto, no es necesario en el schema
        var updateConversationStateSchema = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""field"": {
                    ""type"": ""string"",
                    ""description"": ""Nombre del campo de contexto.""
                },
                ""value"": {
                    ""type"": ""string"",
                    ""description"": ""Valor del campo de contexto. Para fechas usa formato YYYY-MM-DD, para horas usa formato HH:mm, para números solo el valor numérico.""
                }
            },
            ""required"": [""field"", ""value""]
        }");

        _availableTools.Add(new ToolDefinition
        {
            Name = "update_conversation_state",
            Description = "OBLIGATORIO: Guarda información importante del cliente en el contexto de la conversación. DEBES usar esta herramienta INMEDIATAMENTE cuando el cliente mencione información relevante. Consulta el system prompt para conocer los campos específicos que debes detectar según el tipo de negocio. Campos genéricos siempre disponibles: customerName (nombre), phone (teléfono), email (correo), service (servicio/entidad principal), desiredDate (fecha deseada), desiredTime (hora deseada), durationMinutes (duración). IMPORTANTE: No inventar valores aunque sea requerido. El sistema mapeará automáticamente los campos a las propiedades correspondientes del estado de conversación.",
            ParametersSchema = updateConversationStateSchema
        });
    }

    /// <summary>
    /// Enriquece los parámetros de reserva desde el contexto de la conversación si están faltantes.
    /// Encapsula la lógica de completado de parámetros dentro de la herramienta.
    /// Retorna un nuevo JsonElement con los parámetros completados.
    /// </summary>
    private async Task<JsonElement> EnrichReservationParametersFromContextAsync(
        JsonElement args,
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        if (!conversationId.HasValue)
        {
            return args; // No hay contexto disponible, retornar argumentos originales
        }

        // Deserializar a diccionario mutable para poder modificarlo
        var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(args.GetRawText())
            ?? new Dictionary<string, object>();

        // Obtener estado de conversación
        var state = await _contextService.GetAsync(conversationId.Value);

        // Completar service desde el estado si falta
        if (!argsDict.ContainsKey("service") || 
            string.IsNullOrWhiteSpace(argsDict["service"]?.ToString()))
        {
            if (!string.IsNullOrWhiteSpace(state.PrimaryEntity))
            {
                argsDict["service"] = state.PrimaryEntity;
                _logger.LogDebug("Parámetro 'service' completado desde estado: {Service}", state.PrimaryEntity);
            }
        }

        // Completar date desde el estado si falta
        if (!argsDict.ContainsKey("date") || 
            string.IsNullOrWhiteSpace(argsDict["date"]?.ToString()))
        {
            if (state.DesiredDate.HasValue)
            {
                argsDict["date"] = state.DesiredDate.Value.ToString("yyyy-MM-dd");
                _logger.LogDebug("Parámetro 'date' completado desde estado: {Date}", state.DesiredDate.Value);
            }
        }

        // Completar time desde el estado si falta
        if (!argsDict.ContainsKey("time") || 
            string.IsNullOrWhiteSpace(argsDict["time"]?.ToString()))
        {
            if (state.DesiredTime.HasValue)
            {
                argsDict["time"] = state.DesiredTime.Value.ToString("HH:mm");
                _logger.LogDebug("Parámetro 'time' completado desde estado: {Time}", state.DesiredTime.Value);
            }
        }

        // Completar durationMinutes desde el estado si falta
        if (!argsDict.ContainsKey("durationMinutes") || 
            argsDict["durationMinutes"] == null)
        {
            if (state.DurationMinutes.HasValue)
            {
                argsDict["durationMinutes"] = state.DurationMinutes.Value;
                _logger.LogDebug("Parámetro 'durationMinutes' completado desde estado: {Duration}", state.DurationMinutes.Value);
            }
        }

        // Reconstruir JsonElement desde el diccionario modificado
        var enrichedJson = JsonSerializer.Serialize(argsDict);
        return JsonSerializer.Deserialize<JsonElement>(enrichedJson);
    }

}
