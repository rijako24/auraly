using System.Text.Json;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class PrepareReservationChangeTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly ICustomerReservationResolver _reservationResolver;

    public PrepareReservationChangeTool(
        IReservationService reservations,
        ICustomerReservationResolver reservationResolver)
    {
        _reservations = reservations;
        _reservationResolver = reservationResolver;
    }

    public string Name => "prepare_reservation_change";

    public string Description =>
        "Validates a requested change to an existing customer reservation without applying it. " +
        "Can validate service, date, time, and add-ons together.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional internal UUID; omit when there is only one reservation in ESTADO RESERVA." },
            "service": { "type": "string", "description": "Optional new exact or natural service name." },
            "date": { "type": "string", "description": "Optional new date in YYYY-MM-DD format." },
            "time": { "type": "string", "description": "Optional new time in HH:mm format." },
            "add_ons": { "type": "string", "description": "Optional comma-separated add-on names." },
            "add_ons_mode": { "type": "string", "enum": ["add", "remove", "replace"], "description": "How to apply add_ons. Default is add." }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var request = await BuildRequestAsync(
            arguments,
            ctx,
            _reservationResolver,
            apply: false,
            cancellationToken);
        if (request.ErrorJson is not null)
            return request.ErrorJson;

        var result = await _reservations.UpdateReservationAsync(request.Request!, cancellationToken);
        if (!result.Success)
            return ToolResultHelper.Error(result.ErrorCode!, result.ErrorMessage!, result.Hint, recoverable: true);

        return ToolResultHelper.OkWithLlm(ToPayload(result), ToLlmPayload(result));
    }

    internal static async Task<(UpdateReservationChangeRequest? Request, string? ErrorJson)> BuildRequestAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        ICustomerReservationResolver reservationResolver,
        bool apply,
        CancellationToken cancellationToken)
    {
        ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr);
        reservationIdStr = string.IsNullOrWhiteSpace(reservationIdStr) ? null : reservationIdStr;

        var resolved = await reservationResolver.ResolveAsync(ctx, reservationIdStr, cancellationToken);
        if (!resolved.Success)
            return (null, resolved.ErrorJson);

        ToolResultHelper.TryGetString(arguments, "service", out var service);
        ToolResultHelper.TryGetString(arguments, "date", out var dateStr);
        ToolResultHelper.TryGetString(arguments, "time", out var timeStr);
        ToolResultHelper.TryGetString(arguments, "add_ons", out var addOns);
        ToolResultHelper.TryGetString(arguments, "add_ons_mode", out var addOnsMode);

        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(dateStr))
        {
            if (!AgentDateRules.TryParseDate(dateStr, out var parsedDate))
                return (null, ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD.", recoverable: true));
            if (AgentDateRules.IsPastDate(parsedDate, ctx.BusinessToday))
                return (null, ToolResultHelper.Error("past_date", "New date must be today or in the future.", recoverable: true));
            date = parsedDate;
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeOnly.TryParse(timeStr, out var parsedTime))
                return (null, ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm.", recoverable: true));
            time = parsedTime;
        }

        if (string.IsNullOrWhiteSpace(service)
            && date is null
            && time is null
            && string.IsNullOrWhiteSpace(addOns))
        {
            return (null, ToolResultHelper.MissingPrerequisites(["service/date/time/add_ons"]));
        }

        return (new UpdateReservationChangeRequest(
            resolved.Reservation!.ReservationId,
            string.IsNullOrWhiteSpace(service) ? null : service,
            date,
            time,
            string.IsNullOrWhiteSpace(addOns) ? null : addOns,
            string.IsNullOrWhiteSpace(addOnsMode) ? null : addOnsMode,
            apply), null);
    }

    internal static object ToPayload(UpdateReservationChangeResult result) => new
    {
        reservation_id = result.ReservationId,
        service = result.ServiceName,
        date = result.Date?.ToString("yyyy-MM-dd"),
        time = result.Time?.ToString("HH:mm"),
        employee = result.EmployeeName,
        duration_minutes = result.DurationMinutes,
        add_ons = result.AddOns,
        new_total = result.NewTotal,
        payment_policy = result.PaymentPolicy,
        applied = result.Applied
    };

    internal static object ToLlmPayload(UpdateReservationChangeResult result) => new
    {
        reservation_id = result.ReservationId,
        summary = CustomerReservationChangeSummary(result),
        payment_policy = result.PaymentPolicy,
        applied = result.Applied
    };

    private static string CustomerReservationChangeSummary(UpdateReservationChangeResult result)
    {
        var date = result.Date?.ToString("yyyy-MM-dd") ?? "sin fecha";
        var time = result.Time?.ToString("HH:mm") ?? "sin hora";
        var addOns = result.AddOns.Count == 0 ? "sin complementos" : string.Join(", ", result.AddOns);
        return $"{date} {time} {result.ServiceName}; complementos: {addOns}";
    }
}
