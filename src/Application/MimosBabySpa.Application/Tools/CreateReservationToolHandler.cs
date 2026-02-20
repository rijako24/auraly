using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Handler para la herramienta create_reservation.
/// 
/// Esta herramienta crea una reserva real en el backend.
/// Solo se puede ejecutar cuando TODAS las condiciones se cumplen.
/// 
/// PRINCIPIOS:
/// - Solo se ejecuta si FlowEngine.CanCreateReservation() = true
/// - Requiere confirmación EXPLÍCITA del usuario
/// - Requiere disponibilidad confirmada por el backend
/// - El backend es la ÚNICA autoridad para crear reservas
/// - No puede bypassear validaciones
/// </summary>
public class CreateReservationToolHandler : BaseToolHandler
{
    private readonly IReservationService _reservationService;
    private readonly IFlowEngine _flowEngine;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;

    public override string FunctionName => "create_reservation";

    public CreateReservationToolHandler(
        IConversationStateManager stateManager,
        ILogger<CreateReservationToolHandler> logger,
        IReservationService reservationService,
        IFlowEngine flowEngine,
        IUnitOfWork unitOfWork,
        IEmployeeAssignmentService employeeAssignmentService)
        : base(stateManager, logger)
    {
        _reservationService = reservationService;
        _flowEngine = flowEngine;
        _unitOfWork = unitOfWork;
        _employeeAssignmentService = employeeAssignmentService;
    }

    public override FunctionDefinition GetDefinition()
    {
        return new FunctionDefinition
        {
            Name = FunctionName,
            Description = @"Crea una reserva REAL en el sistema después de que el usuario confirmó explícitamente.

REGLAS CRÍTICAS (INFLEXIBLES):
- Solo llamar después de confirmación EXPLÍCITA del usuario (""sí, reserva"", ""confirmo"", ""adelante"")
- Solo llamar si la disponibilidad fue confirmada por el backend (is_available = true)
- NUNCA crear reservas especulativas o ""por si acaso""
- NUNCA confirmar ANTES de que esta función retorne success = true
- Todos los datos (service, date, time) deben estar en el estado
- Si el backend retorna error, la reserva NO se creó (reportar el error al usuario)

CUANDO USAR:
✓ Usuario dijo explícitamente ""sí, confirma la reserva""
✓ Usuario dijo ""adelante"", ""procede"", ""hazlo""
✓ Disponibilidad ya fue verificada (is_available = true)
✓ Todos los datos están completos

CUANDO NO USAR:
❌ Usuario solo preguntó por disponibilidad
❌ Usuario dijo ""déjame pensarlo"" o ""no estoy seguro""
❌ No se verificó disponibilidad aún
❌ Faltan datos requeridos

FLUJO:
1. Verificar que se puede crear (CanCreateReservation = true)
2. Llamar al backend con TODOS los datos
3. Si success = true: ""✓ Reserva confirmada [ID]""
4. Si success = false: ""✗ Error: [mensaje del backend]""
5. NUNCA mentir sobre el resultado",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    // No requiere parámetros adicionales, usa el estado
                },
                required = new string[] { }
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
            // Verificación crítica: ¿Se puede crear la reserva?
            if (!_flowEngine.CanCreateReservation(context.State, context.RequiredFields))
            {
                var missingFields = _flowEngine.GetMissingFields(context.State, context.RequiredFields);
                var reason = BuildCannotCreateReason(context.State, missingFields);
                
                _logger.LogWarning("Intento de crear reserva cuando no se puede. Razón: {Reason}", reason);
                
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = $"No se puede crear la reserva. {reason}"
                };
            }

            // Extraer datos del estado (ya validados por FlowEngine)
            var serviceName = context.State.Service!;
            var date = context.State.DesiredDate!.Value;
            var time = context.State.DesiredTime!.Value;
            var reservationDateTime = date.ToDateTime(time);

            // Obtener servicio desde BD para DurationMinutes (fuente de verdad: catálogo)
            var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                context.BusinessId, serviceName);
            
            if (service == null)
            {
                _logger.LogError("Servicio no encontrado: {ServiceName}", serviceName);
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = $"Error: El servicio '{serviceName}' no existe en el sistema."
                };
            }

            var duration = service.DurationMinutes > 0 ? service.DurationMinutes : 60;

            var serviceId = service.ServiceId;

            // Obtener empleado disponible
            var endTime = reservationDateTime.AddMinutes(duration);
            var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                context.BusinessId,
                serviceId,
                reservationDateTime,
                endTime,
                cancellationToken);

            if (employee == null)
            {
                _logger.LogError("No hay empleado disponible para el servicio {ServiceName} en {DateTime}", 
                    serviceName, reservationDateTime);
                return new ToolExecutionResult
                {
                    Success = false,
                    Message = "Error: No hay empleado disponible para este horario. Por favor intenta con otra fecha u hora."
                };
            }

            // Resolver add-ons seleccionados (state.Attributes["SelectedAddOns"] = CSV de nombres)
            var addOnServiceIds = await ResolveSelectedAddOnsAsync(context.BusinessId, context.State, cancellationToken);

            // Construir metadata con información del perfil (de forma genérica)
            var metadata = BuildReservationMetadata(context);

            _logger.LogInformation(
                "Creando reserva: Service={Service}, Date={Date}, Time={Time}, Duration={Duration}, Employee={Employee}",
                serviceName, date, time, duration, employee.Name);

            // Crear objeto Reservation
            var reservation = new Domain.Entities.Reservation
            {
                ReservationId = Guid.NewGuid(),
                BusinessId = context.BusinessId,
                ServiceId = serviceId,
                EmployeeId = employee.EmployeeId,
                ReservationDateTime = reservationDateTime,
                DurationMinutes = duration,
                Status = Domain.Enums.ReservationStatus.Pending,
                ConversationId = context.ConversationId,
                CreatedAt = DateTime.UtcNow
            };

            // Llamar al backend (ÚNICA AUTORIDAD para crear reservas)
            var reservationDto = await _reservationService.CreateReservationAsync(
                reservation,
                metadata,
                addOnServiceIds.Any() ? addOnServiceIds : null,
                cancellationToken);

            // Actualizar el estado con la reserva creada
            context.State.ReservationCreated = true;
            context.State.ReservationId = reservationDto.ReservationId;
            context.State.CurrentStage = Domain.Models.TransactionStage.BookingCompleted;
            context.State.UpdatedAt = DateTime.UtcNow;
            context.State.Version++;

            _logger.LogInformation(
                "Reserva creada exitosamente: ReservationId={ReservationId}",
                reservationDto.ReservationId);

            var successMessage = $"✓ Reserva confirmada exitosamente" +
                                $"\nServicio: {serviceName}" +
                                (addOnServiceIds.Any()
                                    ? $"\nAdd-ons: {string.Join(", ", await GetAddOnNamesAsync(context.BusinessId, addOnServiceIds))}"
                                    : "") +
                                $"\nFecha: {date:dd/MM/yyyy}" +
                                $"\nHora: {time:HH:mm}" +
                                $"\nEmpleado: {employee.Name}" +
                                $"\nID de reserva: {reservationDto.ReservationId}";

            return new ToolExecutionResult
            {
                Success = true,
                Message = successMessage,
                StateModified = true,
                Data = new Dictionary<string, object>
                {
                    { "reservation_id", reservationDto.ReservationId },
                    { "service", serviceName },
                    { "date", date.ToString("yyyy-MM-dd") },
                    { "time", time.ToString("HH:mm") },
                    { "duration_minutes", duration },
                    { "employee_name", employee.Name },
                    { "customer_name", context.State.CustomerName ?? "N/A" },
                    { "phone", context.State.Phone }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear reserva");
            
            // Marcar que la reserva NO se creó
            context.State.ReservationCreated = false;
            context.State.ReservationId = null;
            
            return new ToolExecutionResult
            {
                Success = false,
                Message = $"Error al crear la reserva: {ex.Message}. " +
                         "Por favor verifica los datos e intenta nuevamente.",
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Construye el mensaje explicando por qué no se puede crear la reserva
    /// </summary>
    private string BuildCannotCreateReason(
        Domain.Models.ConversationState state,
        List<string> missingFields)
    {
        var reasons = new List<string>();

        if (!state.ReservationConfirmed)
        {
            reasons.Add("el usuario no ha confirmado explícitamente la reserva");
        }

        if (!state.AvailabilityConfirmed)
        {
            reasons.Add("la disponibilidad no ha sido confirmada por el backend");
        }

        if (missingFields.Any())
        {
            reasons.Add($"faltan campos requeridos: {string.Join(", ", missingFields)}");
        }

        if (state.ReservationCreated)
        {
            reasons.Add("la reserva ya fue creada anteriormente");
        }

        return reasons.Any() 
            ? string.Join("; ", reasons) 
            : "condiciones no cumplidas";
    }

    /// <summary>
    /// Parsea SelectedAddOns del estado (CSV de nombres) y resuelve a ServiceIds.
    /// Ignora nombres que no existan en el catálogo.
    /// </summary>
    private async Task<List<Guid>> ResolveSelectedAddOnsAsync(
        Guid businessId,
        Domain.Models.ConversationState state,
        CancellationToken cancellationToken)
    {
        var selectedAddOns = state.GetAttribute("SelectedAddOns");
        if (string.IsNullOrWhiteSpace(selectedAddOns))
            return [];

        var names = selectedAddOns
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var ids = new List<Guid>();
        foreach (var name in names)
        {
            var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, name.Trim());
            if (service != null)
                ids.Add(service.ServiceId);
            else
                _logger.LogWarning("Add-on '{AddOnName}' no encontrado en catálogo, omitiendo", name);
        }
        return ids;
    }

    /// <summary>
    /// Obtiene los nombres de servicios para mostrar en el mensaje de éxito.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetAddOnNamesAsync(Guid businessId, List<Guid> addOnServiceIds)
    {
        var names = new List<string>();
        foreach (var id in addOnServiceIds)
        {
            var service = await _unitOfWork.Services.GetByIdAsync(id);
            if (service != null)
                names.Add(service.ServiceName);
        }
        return names;
    }

    /// <summary>
    /// Construye el metadata de la reserva incluyendo información del estado
    /// de forma genérica (sin hardcodear campos específicos de negocio)
    /// </summary>
    private Dictionary<string, string> BuildReservationMetadata(ToolExecutionContext context)
    {
        var metadata = new Dictionary<string, string>();

        // Agregar información de identidad
        if (!string.IsNullOrWhiteSpace(context.State.CustomerName))
        {
            metadata["CustomerName"] = context.State.CustomerName;
        }

        if (!string.IsNullOrWhiteSpace(context.State.Email))
        {
            metadata["Email"] = context.State.Email;
        }

        if (!string.IsNullOrWhiteSpace(context.State.Phone))
        {
            metadata["Phone"] = context.State.Phone;
        }

        // Agregar todos los atributos de negocio (genérico)
        foreach (var attribute in context.State.Attributes)
        {
            metadata[attribute.Key] = attribute.Value;
        }

        return metadata;
    }
}
