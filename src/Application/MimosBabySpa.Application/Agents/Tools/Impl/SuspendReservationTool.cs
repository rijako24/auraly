using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Suspende (pone en espera) una reserva existente.
/// Usar cuando el cliente dice "avisa después" o "no puedo ir, lo dejo pendiente".
/// </summary>
public sealed class SuspendReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;

    public SuspendReservationTool(IReservationService reservations) => _reservations = reservations;

    public string Name => "suspend_reservation";

    public string Description =>
        "Suspends (puts on hold) an existing reservation when the customer cannot attend and wants to reschedule later.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "UUID of the reservation to suspend" }
          },
          "required": ["reservation_id"]
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

        var success = await _reservations.SuspendAsync(reservationId, cancellationToken);

        if (!success)
            return ToolResultHelper.Error("suspend_failed",
                "The reservation could not be suspended.",
                "Verify the reservation ID is correct and the reservation is in an active state.");

        return ToolResultHelper.Ok(new { reservation_id = reservationId, status = "suspended" });
    }
}
