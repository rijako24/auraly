using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Cambia la fecha y hora de una reserva existente.
/// Pre-condición: debe existir una reserva confirmada para la conversación.
/// </summary>
public sealed class RescheduleReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly IConversationStateManager _stateManager;

    public RescheduleReservationTool(IReservationService reservations, IConversationStateManager stateManager)
    {
        _reservations = reservations;
        _stateManager = stateManager;
    }

    public string Name => "reschedule_reservation";

    public string Description =>
        "Changes the date and time of an existing reservation. " +
        "Call check_availability first to confirm the new slot is open.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "UUID of the reservation to reschedule" },
            "new_date": { "type": "string", "description": "New date in YYYY-MM-DD format" },
            "new_time": { "type": "string", "description": "New time in HH:mm format" }
          },
          "required": ["reservation_id", "new_date", "new_time"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr))
            return ToolResultHelper.Error("invalid_args", "'reservation_id' is required.");

        if (!Guid.TryParse(reservationIdStr, out var reservationId))
            return ToolResultHelper.Error("invalid_args", $"'{reservationIdStr}' is not a valid reservation ID.");

        if (!ToolResultHelper.TryGetString(arguments, "new_date", out var dateStr))
            return ToolResultHelper.Error("invalid_args", "'new_date' is required.");

        if (!ToolResultHelper.TryGetString(arguments, "new_time", out var timeStr))
            return ToolResultHelper.Error("invalid_args", "'new_time' is required.");

        if (!DateOnly.TryParse(dateStr, out var newDate))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD.");

        if (newDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return ToolResultHelper.Error("past_date", "New date must be today or in the future.");

        if (!TimeOnly.TryParse(timeStr, out var newTime))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm.");

        var success = await _reservations.RescheduleAsync(reservationId, newDate, newTime, cancellationToken);

        if (!success)
            return ToolResultHelper.Error("reschedule_failed",
                "The reservation could not be rescheduled.",
                "Verify the reservation ID is correct and the new slot is available.");

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            new_date = dateStr,
            new_time = timeStr
        });
    }
}
