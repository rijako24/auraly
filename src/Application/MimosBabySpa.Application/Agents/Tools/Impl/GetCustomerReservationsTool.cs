using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class GetCustomerReservationsTool : IAgentTool
{
    private readonly IReservationLifecycleService _reservationLifecycle;

    public GetCustomerReservationsTool(IReservationLifecycleService reservationLifecycle)
    {
        _reservationLifecycle = reservationLifecycle;
    }

    public string Name => "get_customer_reservations";

    public string Description =>
        "Returns upcoming reservations that belong to the current customer channel in the current business. " +
        "Uses tenant and phone from session context; never requires business_id or customer phone from the model.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var session = await _reservationLifecycle.ResolveForSessionAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            ctx.ChannelPhone,
            ctx.BusinessToday,
            cancellationToken);

        ctx.ManageableReservations = session.ManageableReservations;

        return ToolResultHelper.OkWithLlm(new
        {
            count = session.ManageableReservations.Count,
            reservations = session.ManageableReservations.Select(ToDto)
        }, new
        {
            count = session.ManageableReservations.Count,
            reservations = session.ManageableReservations.Select(r =>
                ReservationTemporalFormatter.FormatLine(r, ctx.BusinessToday))
        });
    }

    private static object ToDto(Domain.Entities.Reservation reservation)
    {
        var addOns = reservation.AddOns
            .Select(a => a.AddOnService?.ServiceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return new
        {
            reservation_id = reservation.ReservationId,
            service = reservation.Service?.ServiceName ?? reservation.GetServiceName(),
            date = reservation.ReservationDateTime.HasValue
                ? DateOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("yyyy-MM-dd")
                : null,
            time = reservation.ReservationDateTime.HasValue
                ? TimeOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("HH:mm")
                : null,
            status = reservation.Status.ToString(),
            add_ons = addOns
        };
    }
}
