using System.Text.Json;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Reservation;

public sealed class ListCustomerReservationsOperation : IAgentOperation
{
    private readonly IReservationLifecycleService _reservations;

    public ListCustomerReservationsOperation(IReservationLifecycleService reservations) =>
        _reservations = reservations;

    public OperationDescriptor Descriptor { get; } = new(
        "reservation.list",
        """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""",
        ["reservation.listed", "reservation.list_failed"],
        [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session
            ?? throw new InvalidOperationException("reservation.list requires a conversation session.");
        try
        {
            var result = await _reservations.ResolveForSessionAsync(
                context.ConversationId,
                context.BusinessId,
                session.ChannelPhone,
                context.BusinessToday,
                cancellationToken);
            session.ManageableReservations = result.ManageableReservations;
            return OperationOutcome.Ok("reservation.listed", new
            {
                count = result.ManageableReservations.Count,
                reservations = result.ManageableReservations.Select(reservation => new
                {
                    reservation_id = reservation.ReservationId,
                    service = reservation.Service?.ServiceName ?? reservation.GetServiceName(),
                    date = reservation.ReservationDateTime.HasValue
                        ? DateOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("yyyy-MM-dd")
                        : null,
                    time = reservation.ReservationDateTime.HasValue
                        ? TimeOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("HH:mm")
                        : null,
                    status = reservation.Status.ToString()
                })
            });
        }
        catch (Exception exception)
        {
            return OperationOutcome.Fail("reservation.list_failed", exception.Message, true);
        }
    }
}
