using System.Globalization;
using System.Text.Json;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Agents.Gating;
using Auraly.Platform.Application.Agents.Templates;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Agents.Operations.Availability;

public static class AvailabilityOutcomeCodes
{
    public const string ExactTimeAvailable = "availability.exact_time_available";
    public const string OptionsAvailable = "availability.options_available";
    public const string RequestedTimeUnavailable = "availability.requested_time_unavailable";
    public const string NoAvailability = "availability.none";
}

public sealed class CheckAvailabilityOperation : IAgentOperation
{
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IEmployeeAssignmentService _employeeAssignment;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _serviceNameResolver;

    public CheckAvailabilityOperation(
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment,
        IUnitOfWork unitOfWork,
        ServiceNameResolver serviceNameResolver)
    {
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
        _unitOfWork = unitOfWork;
        _serviceNameResolver = serviceNameResolver;
    }

    public OperationDescriptor Descriptor { get; } = new(
        "reservation.check_availability",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "service": { "type": "string" },
            "date": { "type": "string" },
            "time": { "type": ["string", "null"] }
          },
          "required": ["service", "date"]
        }
        """,
        [
            AvailabilityOutcomeCodes.ExactTimeAvailable,
            AvailabilityOutcomeCodes.OptionsAvailable,
            AvailabilityOutcomeCodes.RequestedTimeUnavailable,
            AvailabilityOutcomeCodes.NoAvailability,
            "input.invalid",
            "input.invalid_date",
            "input.past_date",
            "input.invalid_time",
            "catalog.service_unresolved"
        ],
        ["reservation.availability.read"],
        ["availability_slots"],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var service = ReadString(input, "service");
        var dateText = ReadString(input, "date");
        var timeText = ReadString(input, "time");

        if (string.IsNullOrWhiteSpace(service))
            return OperationOutcome.Fail("input.invalid", "Parameter 'service' is required.");
        if (string.IsNullOrWhiteSpace(dateText))
            return OperationOutcome.Fail("input.invalid", "Parameter 'date' is required.");

        if (!AgentDateRules.TryParseDate(dateText, out var date))
        {
            return OperationOutcome.Fail(
                "input.invalid_date",
                $"'{dateText}' is not a valid date.",
                recoverable: true,
                remediationSignal: "facts.collect_valid_date",
                context: new { expectedFormat = "yyyy-MM-dd" });
        }

        if (AgentDateRules.IsPastDate(date, context.BusinessToday))
            return OperationOutcome.Fail("input.past_date", "The date must be today or in the future.");

        var canonicalService = await _serviceNameResolver.ResolveAsync(
            context.BusinessId,
            service,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(canonicalService))
        {
            return OperationOutcome.Fail(
                "catalog.service_unresolved",
                "Service selection could not be resolved against the active catalog.",
                recoverable: true,
                remediationSignal: "catalog.service_mentioned",
                context: new { text = service });
        }

        TimeSpan? time = null;
        if (!string.IsNullOrWhiteSpace(timeText))
        {
            if (!TimeSpan.TryParse(timeText, out var parsedTime))
            {
                return OperationOutcome.Fail(
                    "input.invalid_time",
                    $"'{timeText}' is not a valid time.",
                    recoverable: true,
                    remediationSignal: "facts.collect_valid_time",
                    context: new { expectedFormat = "HH:mm" });
            }

            time = parsedTime;
        }

        var policy = await _schedulingPolicy.GetAsync(context.BusinessId, cancellationToken);
        var result = await _availability.CheckAvailabilityAsync(
            context.BusinessId,
            canonicalService,
            date.ToDateTime(TimeOnly.MinValue),
            time,
            policy,
            cancellationToken);

        Guid? preferredEmployeeId = null;
        if (result.IsAvailable && time.HasValue)
        {
            var serviceEntity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                context.BusinessId,
                canonicalService);
            if (serviceEntity is not null)
            {
                var duration = serviceEntity.DurationMinutes > 0 ? serviceEntity.DurationMinutes : 60;
                var start = date.ToDateTime(TimeOnly.FromTimeSpan(time.Value));
                var end = start.AddMinutes(duration + Math.Max(0, policy.BufferBetweenAppointmentsMinutes));
                var employee = await _employeeAssignment.FindBestAvailableEmployeeAsync(
                    context.BusinessId,
                    serviceEntity.ServiceId,
                    start,
                    end,
                    cancellationToken);
                preferredEmployeeId = employee?.EmployeeId;
            }
        }

        var options = BuildPresentationOptions(result);
        var exactTimeAvailable = time.HasValue && result.IsAvailable;
        var requestedTimeUnavailable = time.HasValue && !result.IsAvailable && options.Count > 0;
        var optionsAvailable = !time.HasValue && result.IsAvailable && options.Count > 0;

        var outcomeCode = exactTimeAvailable
            ? AvailabilityOutcomeCodes.ExactTimeAvailable
            : requestedTimeUnavailable
                ? AvailabilityOutcomeCodes.RequestedTimeUnavailable
                : optionsAvailable
                    ? AvailabilityOutcomeCodes.OptionsAvailable
                    : AvailabilityOutcomeCodes.NoAvailability;

        var presentations = BuildPresentations(
            result,
            canonicalService,
            date,
            options,
            requestedTimeUnavailable,
            optionsAvailable);
        var effects = exactTimeAvailable
            ? [BuildVerificationEffect(context, canonicalService, dateText, timeText!)]
            : Array.Empty<OperationEffect>();

        return OperationOutcome.Ok(
            outcomeCode,
            new
            {
                isAvailable = result.IsAvailable,
                isBookingConfirmed = false,
                slotHeld = false,
                availabilityChecked = exactTimeAvailable,
                service = result.RequestServiceName ?? canonicalService,
                date = result.RequestDateString,
                time = result.RequestTimeString,
                availableWindows = result.AvailableWindows,
                availableOptions = result.AvailableOptions,
                option = result.Option,
                requestedOption = result.RequestedOption,
                optionCount = options.Count,
                preferredEmployeeId,
                message = result.ResponseMessage
            },
            presentations,
            effects);
    }

    private static IReadOnlyList<OperationPresentation> BuildPresentations(
        AvailabilityResult result,
        string canonicalService,
        DateOnly date,
        IReadOnlyList<string> options,
        bool requestedTimeUnavailable,
        bool optionsAvailable)
    {
        if ((!requestedTimeUnavailable && !optionsAvailable) || options.Count == 0)
            return [];

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["service_name"] = result.RequestServiceName ?? canonicalService,
            ["date_formatted"] = date.ToString("dd/MM/yyyy"),
            ["options"] = options.Select(static value => (object)value).ToList()
        };
        if (requestedTimeUnavailable)
        {
            data["intro_message"] =
                "El horario pedido no esta disponible; estos son los espacios libres ese dia";
        }

        return
        [
            new OperationPresentation(
                "availability_slots",
                data,
                FragmentRenderMode.Exclusive,
                FragmentPriority.Required)
        ];
    }

    private static SaveVerificationEffect BuildVerificationEffect(
        OperationContext context,
        string service,
        string date,
        string time)
    {
        var roles = new FactRoleIndex(context.Config.FactSchema);
        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

        return new SaveVerificationEffect(
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(serviceKey, service),
                new KeyValuePair<string, string>(dateKey, date),
                new KeyValuePair<string, string>(timeKey, time)),
            VerificationTtl.AvailabilityChecked);
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

    private static string FormatPresentationTime(string value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : value;

    private static string? ReadString(JsonElement input, string property)
    {
        if (!input.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    }
}
