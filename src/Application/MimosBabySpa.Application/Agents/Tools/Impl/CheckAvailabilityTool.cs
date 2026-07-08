using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Consulta de disponibilidad - solo lectura. No crea ni modifica reservas.
/// </summary>
[AgentToolMetadata("check_availability", RequiredTemplateIds = new[] { "availability_slots" })]
public sealed class CheckAvailabilityTool : IAgentTool
{
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IEmployeeAssignmentService _employeeAssignment;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationVerificationService _verifications;
    private readonly ServiceNameResolver _serviceNameResolver;

    public CheckAvailabilityTool(
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment,
        IUnitOfWork unitOfWork,
        IConversationVerificationService verifications,
        ServiceNameResolver serviceNameResolver)
    {
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
        _unitOfWork = unitOfWork;
        _verifications = verifications;
        _serviceNameResolver = serviceNameResolver;
    }

    public string Name => "check_availability";

    public IReadOnlyList<string> RequiredTemplateIds => ["availability_slots"];

    public string Description =>
        "Read-only availability lookup for a service on a date. " +
        "If time is provided, validates that exact start time; otherwise returns bookable options for the day. " +
        "Returns verbal_status, available windows/options, and an optional rendered options token.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string", "description": "Exact service name from the catalog" },
            "date": { "type": "string", "description": "Date in YYYY-MM-DD format" },
            "time": { "type": "string", "description": "Optional specific start time in HH:mm format (24h)" }
          },
          "required": ["service", "date"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "service", out var service);
        ToolResultHelper.TryGetString(arguments, "date", out var dateStr);

        if (string.IsNullOrWhiteSpace(service))
            return ToolResultHelper.Error("invalid_args", "Parameter 'service' is required.", recoverable: true);
        if (string.IsNullOrWhiteSpace(dateStr))
            return ToolResultHelper.Error("invalid_args", "Parameter 'date' is required.", recoverable: true);

        if (!AgentDateRules.TryParseDate(dateStr, out var date))
            return ToolResultHelper.ErrorWithNextAction("invalid_date", $"'{dateStr}' is not a valid date.", "collect_valid_date", new { expected_format = "yyyy-MM-dd" });

        if (AgentDateRules.IsPastDate(date, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "The date must be today or in the future.", recoverable: true);

        var canonicalService = await _serviceNameResolver.ResolveAsync(ctx.BusinessId, service, cancellationToken);
        if (string.IsNullOrWhiteSpace(canonicalService))
        {
            return ToolResultHelper.ErrorWithLlm(
                "service_selection_unresolved",
                "Service selection could not be resolved against the active catalog.",
                null,
                new
                {
                    next_action = "resolve_service_selection",
                    text = service
                },
                recoverable: true);
        }

        TimeSpan? time = null;
        ToolResultHelper.TryGetString(arguments, "time", out var timeStr);
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeSpan.TryParse(timeStr, out var parsedTime))
                return ToolResultHelper.ErrorWithNextAction("invalid_time", $"'{timeStr}' is not a valid time.", "collect_valid_time", new { expected_format = "HH:mm" });
            time = parsedTime;
        }

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var result = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId, canonicalService, date.ToDateTime(TimeOnly.MinValue), time, policy, cancellationToken);

        Guid? preferredEmployeeId = null;
        if (result.IsAvailable && time.HasValue)
        {
            var serviceEntity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, canonicalService);
            if (serviceEntity is not null)
            {
                var duration = serviceEntity.DurationMinutes > 0 ? serviceEntity.DurationMinutes : 60;
                var start = date.ToDateTime(TimeOnly.FromTimeSpan(time.Value));
                var end = start.AddMinutes(duration + Math.Max(0, policy.BufferBetweenAppointmentsMinutes));
                var employee = await _employeeAssignment.FindBestAvailableEmployeeAsync(
                    ctx.BusinessId, serviceEntity.ServiceId, start, end, cancellationToken);
                preferredEmployeeId = employee?.EmployeeId;
            }
        }

        var availabilityChecked = time.HasValue && result.IsAvailable;
        if (availabilityChecked)
            RecordAvailabilityVerifications(ctx, canonicalService, dateStr, timeStr, result);

        var optionsForPresentation = BuildPresentationOptions(result);
        var isListMode = !time.HasValue && result.IsAvailable;
        var isUnavailableRequestedTime = time.HasValue && !result.IsAvailable && optionsForPresentation.Count > 0;

        var verbalStatus = result.IsAvailable && time.HasValue
            ? "horario_disponible_no_reservado"
            : isUnavailableRequestedTime
                ? "horario_no_disponible_alternativas"
                : result.IsAvailable
                    ? "opciones_disponibles"
                    : "sin_disponibilidad";

        string? presentationToken = null;
        if (ctx.Turn is not null && optionsForPresentation.Count > 0 && (isListMode || isUnavailableRequestedTime))
        {
            var fragmentData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["service_name"] = result.RequestServiceName ?? canonicalService,
                ["date_formatted"] = date.ToString("dd/MM/yyyy"),
                ["options"] = optionsForPresentation.Select(static s => (object)s).ToList()
            };

            if (isUnavailableRequestedTime)
                fragmentData["intro_message"] =
                    "El horario pedido no esta disponible; estos son los espacios libres ese dia";

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
                ? "Tu respuesta debe ser UNICAMENTE el token presentation_token tal cual. La plantilla ya incluye el aviso del horario pedido y la pregunta de cierre; no agregues texto antes ni despues."
                : "Tu respuesta debe ser UNICAMENTE el token presentation_token tal cual. La plantilla ya cierra con la pregunta para que el cliente elija horario; no agregues texto antes ni despues.";

            return ToolResultHelper.Ok(new
            {
                is_available = result.IsAvailable,
                is_booking_confirmed = false,
                slot_held = false,
                availability_checked = availabilityChecked,
                verbal_status = verbalStatus,
                service = result.RequestServiceName,
                date = result.RequestDateString,
                time = result.RequestTimeString,
                available_windows = result.AvailableWindows,
                available_options = result.AvailableOptions,
                option = result.Option,
                requested_option = result.RequestedOption,
                option_count = optionsForPresentation.Count,
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
            availability_checked = availabilityChecked,
            verbal_status = verbalStatus,
            service = result.RequestServiceName,
            date = result.RequestDateString,
            time = result.RequestTimeString,
            available_windows = result.AvailableWindows,
            available_options = result.AvailableOptions,
            option = result.Option,
            requested_option = result.RequestedOption,
            preferred_employee_id = preferredEmployeeId,
            message = result.ResponseMessage
        });
    }

    private static List<string> BuildPresentationOptions(AvailabilityResult result)
    {
        if (result.AvailableOptions.Count > 0)
        {
            return result.AvailableOptions
                .Select(option => FormatPresentationTime(option.Start))
                .ToList();
        }

        if (result.Option is not null)
            return [FormatPresentationTime(result.Option.Start)];

        if (result.RequestedOption is not null)
            return [FormatPresentationTime(result.RequestedOption.Start)];

        return [];
    }

    private static string FormatPresentationTime(string value)
    {
        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : value;
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
    }
