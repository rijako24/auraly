using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
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
    private readonly IConversationVerificationService _verifications;

    public CheckAvailabilityTool(
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment,
        IUnitOfWork unitOfWork,
        IConversationVerificationService verifications)
    {
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
        _unitOfWork = unitOfWork;
        _verifications = verifications;
    }

    public string Name => "check_availability";

    public string Description =>
        "Read-only availability lookup for a service on a date. " +
        "If time is provided, validates that slot; otherwise returns available slots for the day. " +
        "Returns verbal_status, slot data, and an optional rendered slots token.";

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
            return ToolResultHelper.Error("invalid_args", "Parameter 'service' is required.", recoverable: true);
        if (string.IsNullOrWhiteSpace(dateStr))
            return ToolResultHelper.Error("invalid_args", "Parameter 'date' is required.", recoverable: true);

        if (!AgentDateRules.TryParseDate(dateStr, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD format.", recoverable: true);

        if (AgentDateRules.IsPastDate(date, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "The date must be today or in the future.", recoverable: true);

        TimeSpan? time = null;
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeSpan.TryParse(timeStr, out var parsedTime))
                return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm format.", recoverable: true);
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

        if (result.IsAvailable)
            RecordAvailabilityVerifications(ctx, service, dateStr, timeStr, result);

        var slotsForPresentation = BuildPresentationSlots(result, timeStr);
        var isListMode = !time.HasValue && result.IsAvailable;
        var isUnavailableRequestedTime = time.HasValue && !result.IsAvailable && slotsForPresentation.Count > 0;

        var verbalStatus = result.IsAvailable && time.HasValue
            ? "horario_disponible_no_reservado"
            : isUnavailableRequestedTime
                ? "horario_no_disponible_alternativas"
                : result.IsAvailable
                    ? "slots_disponibles_sin_reservar"
                    : "sin_disponibilidad";

        string? presentationToken = null;
        if (ctx.Turn is not null && slotsForPresentation.Count > 0 && (isListMode || isUnavailableRequestedTime))
        {
            var fragmentData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["service_name"] = result.RequestServiceName ?? service,
                ["date_formatted"] = date.ToString("dd/MM/yyyy"),
                ["slots"] = slotsForPresentation.Select(static s => (object)s).ToList()
            };

            if (isUnavailableRequestedTime)
                fragmentData["intro_message"] =
                    "El horario pedido no está disponible; estos son los libres ese día";

            presentationToken = ctx.Turn.RegisterFragment(
                "SLOTS",
                "availability_slots",
                fragmentData,
                FragmentRenderMode.Inline,
                FragmentPriority.Required);
        }

        if (presentationToken is not null)
        {
            var presentationInstruction = isUnavailableRequestedTime
                ? "Tu respuesta debe ser ÚNICAMENTE el token presentation_token tal cual. La plantilla ya incluye el aviso del horario pedido y la pregunta de cierre; no agregues texto antes ni después."
                : "Tu respuesta debe ser ÚNICAMENTE el token presentation_token tal cual. La plantilla ya cierra con la pregunta para que el cliente elija horario; no agregues texto antes ni después.";

            return ToolResultHelper.Ok(new
            {
                is_available = result.IsAvailable,
                is_booking_confirmed = false,
                slot_held = false,
                verbal_status = verbalStatus,
                service = result.RequestServiceName,
                date = result.RequestDateString,
                time = result.RequestTimeString,
                slot_count = slotsForPresentation.Count,
                preferred_employee_id = preferredEmployeeId,
                presentation_token = presentationToken,
                presentation_instruction = presentationInstruction
            });
        }

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

    private static List<string> BuildPresentationSlots(AvailabilityResult result, string? timeStr)
    {
        if (result.AvailableTimeSlots.Count > 0)
        {
            return result.AvailableTimeSlots
                .Select(s => TimeOnly.TryParse(s, out var parsed) ? parsed.ToString("HH:mm") : s)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(result.RequestTimeString))
            return [TimeOnly.TryParse(result.RequestTimeString, out var parsed) ? parsed.ToString("HH:mm") : result.RequestTimeString];

        if (!string.IsNullOrWhiteSpace(timeStr))
            return [TimeOnly.TryParse(timeStr, out var fromArg) ? fromArg.ToString("HH:mm") : timeStr];

        return [];
    }

    private void RecordAvailabilityVerifications(
        AgentToolContext ctx,
        string service,
        string dateStr,
        string? timeStr,
        AvailabilityResult result)
    {
        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

        var pairs = new List<KeyValuePair<string, string>>
        {
            new(serviceKey, service),
            new(dateKey, dateStr)
        };

        if (!string.IsNullOrWhiteSpace(timeStr))
            pairs.Add(new KeyValuePair<string, string>(timeKey, timeStr));

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(pairs.ToArray()),
            VerificationTtl.AvailabilityChecked);
    }

    private static string? Coalesce(JsonElement args, string property, string? factValue)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(factValue) ? null : factValue;
    }
}
