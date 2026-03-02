using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Handler para la herramienta create_reservation.
///
/// Esta herramienta crea una reserva real en el backend.
/// Solo se invoca cuando el Orchestrator determina que CanCreateReservation = true
/// (evaluación del FlowEngine). El handler ejecuta sin revalidar.
///
/// PRINCIPIOS:
/// - Solo se invoca cuando el Orchestrator lo permite (FlowEvaluation.CanCreateReservation)
/// - Requiere confirmación EXPLÍCITA del usuario (validado por FlowEngine)
/// - Requiere disponibilidad confirmada por el backend (validado por FlowEngine)
/// - El backend es la ÚNICA autoridad para crear reservas
/// - Responsabilidad: traducir contexto → request, delegar al servicio, actualizar estado
/// </summary>
public class CreateReservationToolHandler : BaseToolHandler
{
    private readonly IReservationService _reservationService;

    public override string FunctionName => "create_reservation";

    public CreateReservationToolHandler(
        IConversationStateManager stateManager,
        ILogger<CreateReservationToolHandler> logger,
        IReservationService reservationService)
        : base(stateManager, logger)
    {
        _reservationService = reservationService;
    }

    public override FunctionDefinition GetDefinition()
    {
        return new FunctionDefinition
        {
            Name = FunctionName,
            Description = "Crea una reserva en el backend usando el estado actual. Solo se invoca cuando CanCreateReservation = true.",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            })
        };
    }

    protected override async Task<ToolExecutionResult> ExecuteCoreAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreateReservationRequest(
                context.BusinessId,
                context.ConversationId,
                context.State.Service!,
                context.State.DesiredDate!.Value,
                context.State.DesiredTime!.Value,
                context.State.GetAttribute("SelectedAddOns"),
                context.State.CustomerName,
                context.State.Email,
                context.State.Phone,
                context.State.Attributes);

            var response = await _reservationService.CreateReservationAsync(request, cancellationToken);

            context.State.ReservationCreated = true;
            context.State.ReservationId = response.ReservationId;
            context.State.CurrentStage = Domain.Models.TransactionStage.BookingCompleted;
            context.State.UpdatedAt = DateTime.UtcNow;
            context.State.Version++;

            _logger.LogInformation(
                "Reserva creada exitosamente: ReservationId={ReservationId}",
                response.ReservationId);

            var addOnsLine = response.AddOnNames.Count > 0
                ? $"\nExtras: {string.Join(", ", response.AddOnNames)}"
                : "";

            var successMessage = $"✓ Reserva confirmada exitosamente" +
                                 $"\nServicio: {response.ServiceName}" +
                                 addOnsLine +
                                 $"\nFecha: {response.Date:dd/MM/yyyy}" +
                                 $"\nHora: {response.Time:HH:mm}" +
                                 $"\nEmpleado: {response.EmployeeName}" +
                                 $"\nID de reserva: {response.ReservationId}";

            return new ToolExecutionResult
            {
                Success = true,
                Message = successMessage,
                StateModified = true,
                Data = new Dictionary<string, object>
                {
                    { "reservation_id", response.ReservationId },
                    { "service", response.ServiceName },
                    { "date", response.Date.ToString("yyyy-MM-dd") },
                    { "time", response.Time.ToString("HH:mm") },
                    { "duration_minutes", response.DurationMinutes },
                    { "employee_name", response.EmployeeName },
                    { "customer_name", context.State.CustomerName ?? "N/A" },
                    { "phone", context.State.Phone ?? "N/A" }
                }
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear reserva");
            context.State.ReservationCreated = false;
            context.State.ReservationId = null;

            return new ToolExecutionResult
            {
                Success = false,
                Message = ex.Message,
                Exception = ex
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear reserva");
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
}
