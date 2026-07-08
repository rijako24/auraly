using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Suspende (pone en espera) una reserva existente del cliente.
/// <c>reservation_id</c> es opcional: se resuelve por conversación o teléfono del canal.
/// </summary>
public sealed class SuspendReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly ICustomerReservationResolver _reservationResolver;

    public SuspendReservationTool(
        IReservationService reservations,
        ICustomerReservationResolver reservationResolver)
    {
        _reservations = reservations;
        _reservationResolver = reservationResolver;
    }

    public string Name => "suspend_reservation";

    public string Description =>
        "Sets an existing reservation to suspended status for the current customer. " +
        "reservation_id is optional when there is a single active reservation in session context.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional. Internal UUID; omit when only one reservation is in ESTADO RESERVA." }
          },
          "required": []
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

        var success = await _reservations.SuspendAsync(reservationId, cancellationToken);

        if (!success)
            return ToolResultHelper.ErrorWithNextAction("suspend_failed", "The reservation could not be suspended.", "select_reservation");

        return ToolResultHelper.Ok(new { reservation_id = reservationId, status = "suspended" });
    }
}
