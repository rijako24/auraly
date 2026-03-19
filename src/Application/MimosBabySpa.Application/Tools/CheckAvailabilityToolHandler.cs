using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Handler para verificar disponibilidad de horarios.
///
/// PRINCIPIOS:
/// - Consulta al backend (nunca decide disponibilidad por sí mismo).
/// - Usa IConversationStateUpdater para actualizar AvailabilityConfirmed y AvailableTimeSlots
///   (no mutación directa — respeta la fuente de verdad centralizada).
/// - Domain-agnostic: funciona para cualquier negocio multitenant.
/// - Lee Service/Date/Time del estado de la conversación.
/// </summary>
public class CheckAvailabilityToolHandler : BaseToolHandler
{
    private readonly IAvailabilityService _availabilityService;
    private readonly IFlowEngine _flowEngine;
    private readonly IConversationStateUpdater _stateUpdater;

    public override string FunctionName => "check_availability";

    public CheckAvailabilityToolHandler(
        IConversationStateManager stateManager,
        ILogger<CheckAvailabilityToolHandler> logger,
        IAvailabilityService availabilityService,
        IFlowEngine flowEngine,
        IConversationStateUpdater stateUpdater)
        : base(stateManager, logger)
    {
        _availabilityService = availabilityService;
        _flowEngine          = flowEngine;
        _stateUpdater        = stateUpdater;
    }

    public override FunctionDefinition GetDefinition()
    {
        return new FunctionDefinition
        {
            Name = FunctionName,
            Description = "Verifica disponibilidad para un servicio en una fecha. Solo se invoca cuando CanCheckAvailability = true.",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    service = new { type = "string" },
                    date = new { type = "string" },
                    time = new { type = "string" }
                },
                required = new[] { "service", "date" }
            })
        };
    }

    protected override async Task<ToolExecutionResult> ExecuteCoreAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var service = context.State.Service;
            if (string.IsNullOrWhiteSpace(service))
                return Fail("Falta 'service'. Asegúrate de que el servicio esté en el estado.");

            var dateStr = context.State.DesiredDate?.ToString("yyyy-MM-dd");
            if (string.IsNullOrWhiteSpace(dateStr))
                return Fail("Falta 'date'. Asegúrate de que la fecha esté en el estado.");

            if (!DateOnly.TryParse(dateStr, out var date))
                return Fail($"Fecha inválida: '{dateStr}'. Usar formato YYYY-MM-DD.");

            var timeStr = context.State.DesiredTime?.ToString("HH:mm");
            TimeSpan? time = null;
            if (!string.IsNullOrWhiteSpace(timeStr))
            {
                if (!TimeOnly.TryParse(timeStr, out var timeOnly))
                    return Fail($"Hora inválida: '{timeStr}'. Usar formato HH:MM.");
                time = timeOnly.ToTimeSpan();
            }

            // Verificar con FlowEngine que es posible (re-check: ShouldRecheckAvailability)
            if (!_flowEngine.ShouldRecheckAvailability(context.State))
                return Fail("Faltan datos para verificar disponibilidad (Service y Date son requeridos).");

            _logger.LogInformation(
                "Verificando disponibilidad: Service={Service}, Date={Date}, Time={Time}",
                service, dateStr, timeStr ?? "cualquiera");

            var availability = await _availabilityService.CheckAvailabilityAsync(
                context.BusinessId,
                service,
                date.ToDateTime(TimeOnly.MinValue),
                time,
                policy: null,
                cancellationToken);

            // Actualizar estado vía StateUpdater (fuente de verdad centralizada)
            var slotsStr = availability.AvailableTimeSlots.Count > 0
                ? string.Join(",", availability.AvailableTimeSlots)
                : string.Empty;

            _stateUpdater.ApplyConfirmationFlag(
                context.State,
                "AvailabilityConfirmed",
                availability.IsAvailable,
                slotsStr);

            _logger.LogInformation("Disponibilidad verificada: IsAvailable={IsAvailable}", availability.IsAvailable);

            return new ToolExecutionResult
            {
                Success       = true,
                Message       = availability.ResponseMessage,
                StateModified = true,
                Data = new Dictionary<string, object>
                {
                    { "is_available",    availability.IsAvailable },
                    { "service",         availability.RequestServiceName },
                    { "date",            availability.RequestDateString },
                    { "time",            availability.RequestTimeString ?? "any" },
                    { "suggested_slots", slotsStr }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar disponibilidad");
            return Fail($"Error al verificar disponibilidad: {ex.Message}", ex);
        }
    }

    private static ToolExecutionResult Fail(string message, Exception? ex = null) =>
        new() { Success = false, Message = message, Exception = ex };
}
