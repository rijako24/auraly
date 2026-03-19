using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Puts an existing reservation on hold.
/// Input keys: reservation_id
/// Output ports: success, failure
/// </summary>
public class SuspendAction : IFlowAction
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<SuspendAction> _logger;

    public string ActionType => "suspend";

    public SuspendAction(IReservationService reservationService, ILogger<SuspendAction> logger)
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

        if (!Guid.TryParse(reservationIdStr, out var reservationId))
        {
            _logger.LogWarning("Suspend: invalid or missing reservation_id");
            return FlowActionResult.Failed("Valid reservation_id is required");
        }

        try
        {
            var success = await _reservationService.SuspendAsync(reservationId, ct);

            if (!success)
                return FlowActionResult.Failed("Could not suspend reservation");

            return FlowActionResult.Succeeded(new Dictionary<string, object?>
            {
                ["success"] = "true"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Suspend failed for reservation {ReservationId}", reservationId);
            return FlowActionResult.Failed(ex.Message);
        }
    }
}
