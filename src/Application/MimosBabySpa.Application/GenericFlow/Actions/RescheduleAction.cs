using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Reschedules an existing reservation to a new date/time.
/// Input keys: reservation_id, date, time
/// Output keys: success (bool string)
/// Output ports: success, failure
/// </summary>
public class RescheduleAction : IFlowAction
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<RescheduleAction> _logger;

    public string ActionType => "reschedule";

    public RescheduleAction(IReservationService reservationService, ILogger<RescheduleAction> logger)
    {
        _reservationService = reservationService;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var reservationIdStr = inputs.GetString("reservation_id");
        var date = inputs.GetString("date");
        var time = inputs.GetString("time");

        if (!Guid.TryParse(reservationIdStr, out var reservationId) ||
            string.IsNullOrEmpty(date) || string.IsNullOrEmpty(time))
        {
            _logger.LogWarning("Reschedule: missing or invalid inputs");
            return FlowActionResult.Failed("reservation_id, date and time are required");
        }

        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(time, out var parsedTime))
        {
            _logger.LogWarning("Reschedule: invalid date/time format");
            return FlowActionResult.Failed("Invalid date or time format");
        }

        try
        {
            var success = await _reservationService.RescheduleAsync(reservationId, parsedDate, parsedTime, ct);

            if (!success)
                return FlowActionResult.Failed("Reschedule failed — reservation may not exist or time is unavailable");

            return FlowActionResult.Succeeded(new Dictionary<string, object?>
            {
                ["success"] = "true"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reschedule failed for reservation {ReservationId}", reservationId);
            return FlowActionResult.Failed(ex.Message);
        }
    }
}
