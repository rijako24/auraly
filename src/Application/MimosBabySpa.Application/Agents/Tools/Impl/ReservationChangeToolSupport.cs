using System.Text.Json;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class ReservationChangeToolSupport
{
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
                return (null, ToolResultHelper.ErrorWithNextAction(
                    "invalid_date",
                    $"'{dateStr}' is not a valid date.",
                    "collect_valid_date",
                    new { expected_format = "yyyy-MM-dd" }));
            if (AgentDateRules.IsPastDate(parsedDate, ctx.BusinessToday))
                return (null, ToolResultHelper.Error("past_date", "New date must be today or in the future."));
            date = parsedDate;
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeOnly.TryParse(timeStr, out var parsedTime))
                return (null, ToolResultHelper.ErrorWithNextAction(
                    "invalid_time",
                    $"'{timeStr}' is not a valid time.",
                    "collect_valid_time",
                    new { expected_format = "HH:mm" }));
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

    internal static string BuildErrorResult(UpdateReservationChangeResult result)
    {
        var code = result.ErrorCode ?? "reservation_change_failed";
        var message = result.ErrorMessage ?? "Reservation change could not be completed.";
        var nextAction = code switch
        {
            "date_required" => "collect_reschedule_date",
            "time_required" => "collect_reschedule_time",
            "datetime_required" => "collect_reschedule_datetime",
            "service_not_found" => "select_catalog_service",
            "slot_unavailable" => "offer_alternative_slots",
            "invalid_add_ons" => "select_compatible_add_ons",
            "ambiguous_add_ons" => "clarify_add_on_selection",
            "duplicate_add_on_group" => "select_single_add_on_per_group",
            "reservation_not_manageable" => "human_handoff",
            _ => "resolve_reservation_change_error"
        };

        return ToolResultHelper.ErrorWithNextAction(
            code,
            message,
            nextAction,
            new
            {
                reservation_id = result.ReservationId,
                service = string.IsNullOrWhiteSpace(result.ServiceName) ? null : result.ServiceName,
                date = result.Date?.ToString("yyyy-MM-dd"),
                time = result.Time?.ToString("HH:mm"),
                add_ons = result.AddOns,
                payment_policy = string.IsNullOrWhiteSpace(result.PaymentPolicy) ? null : result.PaymentPolicy
            });
    }

    private static string CustomerReservationChangeSummary(UpdateReservationChangeResult result)
    {
        var date = result.Date?.ToString("yyyy-MM-dd") ?? "missing";
        var time = result.Time?.ToString("HH:mm") ?? "missing";
        var addOns = result.AddOns.Count == 0 ? "none" : string.Join(", ", result.AddOns);
        return $"date={date}; time={time}; service={result.ServiceName}; add_ons={addOns}";
    }
}
