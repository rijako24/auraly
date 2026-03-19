using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;
#pragma warning disable CS1998

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Prepares state for rescheduling an existing reservation.
/// Reads the most recent reservation from context and copies relevant data.
/// Output keys: original_reservation_id, original_service
/// Output flag: is_rescheduling = true on success
/// Output ports: success, failure
/// </summary>
public class SetupRescheduleAction : IFlowAction
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<SetupRescheduleAction> _logger;

    public string ActionType => "setup_reschedule";

    public SetupRescheduleAction(IReservationService reservationService, ILogger<SetupRescheduleAction> logger)
    {
        _reservationService = reservationService;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        // Check if reservation_id is already in state (same-session flow with existing reservation)
        var reservationIdStr = ctx.State.GetVariable("reservation_id");

        if (string.IsNullOrEmpty(reservationIdStr))
        {
            // Try to find from previous session snapshot (if stored as persistent var)
            reservationIdStr = ctx.State.PreviousSession?.PersistentVariables
                .GetValueOrDefault("reservation_id");
        }

        if (string.IsNullOrEmpty(reservationIdStr) || !Guid.TryParse(reservationIdStr, out _))
        {
            // Last resort: look up most recent reservation by businessId + phone
            try
            {
                // Without a reservation_id, we cannot safely identify which reservation to reschedule.
                // The admin should configure the flow to collect reservation_id before invoking this action,
                // or extend IReservationService with a method to look up by customer identifier.
                _logger.LogInformation("SetupReschedule: no reservation_id available for {Identifier}", ctx.UserIdentifier);
                return FlowActionResult.Failed("No reservation_id available — cannot proceed with reschedule");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetupReschedule failed during reservation lookup");
                return FlowActionResult.Failed(ex.Message);
            }
        }

        _logger.LogInformation("SetupReschedule: using reservation {ReservationId} from state", reservationIdStr);
        return FlowActionResult.Succeeded(new Dictionary<string, object?>
        {
            ["original_reservation_id"] = reservationIdStr,
            ["original_service"] = ctx.State.GetVariable("service") ?? string.Empty,
            ["success"] = "true"
        });
    }
}
