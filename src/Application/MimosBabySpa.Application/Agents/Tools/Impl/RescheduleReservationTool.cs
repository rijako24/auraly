using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Cambia la fecha y hora de una reserva existente del cliente.
/// <c>reservation_id</c> es opcional: se resuelve por conversación o teléfono del canal.
/// </summary>
public sealed class RescheduleReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly ICustomerReservationResolver _reservationResolver;

    public RescheduleReservationTool(
        IReservationService reservations,
        ICustomerReservationResolver reservationResolver)
    {
        _reservations = reservations;
        _reservationResolver = reservationResolver;
    }

    public string Name => "reschedule_reservation";

    public string Description =>
        "Updates the date and time of an existing reservation for the current customer. " +
        "reservation_id is optional when there is a single active reservation in session context.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional. Internal UUID; omit when only one reservation is in ESTADO RESERVA." },
            "new_date": { "type": "string", "description": "New date in YYYY-MM-DD format" },
            "new_time": { "type": "string", "description": "New time in HH:mm format" }
          },
          "required": ["new_date", "new_time"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr);
        reservationIdStr = string.IsNullOrWhiteSpace(reservationIdStr) ? null : reservationIdStr;

        var resolved = await _reservationResolver.ResolveAsync(ctx, reservationIdStr, cancellationToken);
        if (!resolved.Success)
            return resolved.ErrorJson!;

        var reservationId = resolved.Reservation!.ReservationId;

        if (!ToolResultHelper.TryGetString(arguments, "new_date", out var dateStr))
            return ToolResultHelper.Error("invalid_args", "'new_date' is required.");

        if (!ToolResultHelper.TryGetString(arguments, "new_time", out var timeStr))
            return ToolResultHelper.Error("invalid_args", "'new_time' is required.");

        if (!AgentDateRules.TryParseDate(dateStr, out var newDate))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD.");

        if (AgentDateRules.IsPastDate(newDate, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "New date must be today or in the future.");

        if (!TimeOnly.TryParse(timeStr, out var newTime))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm.");

        var success = await _reservations.RescheduleAsync(reservationId, newDate, newTime, cancellationToken);

        if (!success)
            return ToolResultHelper.Error("reschedule_failed",
                "The reservation could not be rescheduled.",
                "Verify the new slot is available.");

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            new_date = dateStr,
            new_time = timeStr
        });
    }
}
