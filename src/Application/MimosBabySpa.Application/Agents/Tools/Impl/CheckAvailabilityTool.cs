using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Consulta de disponibilidad — solo lectura. No crea ni modifica reservas.
/// </summary>
public sealed class CheckAvailabilityTool : IAgentTool
{
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IEmployeeAssignmentService _employeeAssignment;
    private readonly IUnitOfWork _unitOfWork;

    public CheckAvailabilityTool(
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment,
        IUnitOfWork unitOfWork)
    {
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
        _unitOfWork = unitOfWork;
    }

    public string Name => "check_availability";

    public string Description =>
        "Checks available time slots for a service on a specific date. " +
        "Read-only: does NOT create or hold a reservation. " +
        "Always call this before confirming any appointment time. " +
        "If time is provided, checks that specific slot. Otherwise returns all available slots for the day.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string", "description": "Exact service name from the catalog" },
            "date": { "type": "string", "description": "Date in YYYY-MM-DD format" },
            "time": { "type": "string", "description": "Optional specific time in HH:mm format (24h)" }
          },
          "required": ["service", "date"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var service = Coalesce(arguments, "service", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service));
        var dateStr = Coalesce(arguments, "date", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate));

        if (string.IsNullOrWhiteSpace(service))
            return ToolResultHelper.Error("invalid_args", "Parameter 'service' is required.");
        if (string.IsNullOrWhiteSpace(dateStr))
            return ToolResultHelper.Error("invalid_args", "Parameter 'date' is required.");

        if (!AgentDateRules.TryParseDate(dateStr, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD format.");

        if (AgentDateRules.IsPastDate(date, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "The date must be today or in the future.");

        TimeSpan? time = null;
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeSpan.TryParse(timeStr, out var parsedTime))
                return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm format.");
            time = parsedTime;
        }

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var result = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId, service, date.ToDateTime(TimeOnly.MinValue), time, policy, cancellationToken);

        Guid? preferredEmployeeId = null;
        if (result.IsAvailable && time.HasValue)
        {
            var serviceEntity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, service);
            if (serviceEntity is not null)
            {
                var duration = serviceEntity.DurationMinutes > 0 ? serviceEntity.DurationMinutes : 60;
                var start = date.ToDateTime(TimeOnly.FromTimeSpan(time.Value));
                var end = start.AddMinutes(duration);
                var employee = await _employeeAssignment.FindBestAvailableEmployeeAsync(
                    ctx.BusinessId, serviceEntity.ServiceId, start, end, cancellationToken);
                preferredEmployeeId = employee?.EmployeeId;
            }
        }

        var verbalStatus = result.IsAvailable && time.HasValue
            ? "horario_disponible_no_reservado"
            : result.IsAvailable
                ? "slots_disponibles_sin_reservar"
                : "sin_disponibilidad";

        return ToolResultHelper.Ok(new
        {
            is_available = result.IsAvailable,
            is_booking_confirmed = false,
            slot_held = false,
            verbal_status = verbalStatus,
            service = result.RequestServiceName,
            date = result.RequestDateString,
            time = result.RequestTimeString,
            available_slots = result.AvailableTimeSlots,
            preferred_employee_id = preferredEmployeeId,
            message = result.ResponseMessage
        });
    }

    private static string? Coalesce(JsonElement args, string property, string? factValue)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(factValue) ? null : factValue;
    }
}
