using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class CustomerReservationResolver : ICustomerReservationResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReservationLifecycleService _reservationLifecycle;

    public CustomerReservationResolver(
        IUnitOfWork unitOfWork,
        IReservationLifecycleService reservationLifecycle)
    {
        _unitOfWork = unitOfWork;
        _reservationLifecycle = reservationLifecycle;
    }

    public async Task<ReservationResolveResult> ResolveAsync(
        AgentToolContext ctx,
        string? reservationIdFromArgs,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(reservationIdFromArgs))
        {
            if (!Guid.TryParse(reservationIdFromArgs, out var explicitId))
            {
                return ReservationResolveResult.Fail(
                    ToolResultHelper.Error("invalid_args", $"'{reservationIdFromArgs}' is not a valid reservation ID."));
            }

            return await ResolveExplicitAsync(ctx, explicitId, ct);
        }

        var resolvedFromContext = ResolveFromList(ctx.ManageableReservations);
        if (resolvedFromContext is not null)
            return resolvedFromContext;

        var session = await _reservationLifecycle.ResolveForSessionAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            ctx.ChannelPhone,
            ctx.BusinessToday,
            ct);

        var resolvedFromSession = ResolveFromList(session.ManageableReservations);
        if (resolvedFromSession is not null)
            return resolvedFromSession;

        return ReservationResolveResult.Fail(
            ToolResultHelper.Error(
                "no_manageable_reservation",
                "No confirmed reservation was found for this customer.",
                "The customer may need to book first, or provide which appointment (date and service) they mean."));
    }

    private async Task<ReservationResolveResult> ResolveExplicitAsync(
        AgentToolContext ctx,
        Guid reservationId,
        CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation is null || reservation.BusinessId != ctx.BusinessId)
        {
            return ReservationResolveResult.Fail(
                ToolResultHelper.Error("reservation_not_found", "Reservation was not found."));
        }

        if (!PhoneMatches(reservation.CustomerPhoneSnapshot, ctx.ChannelPhone))
        {
            return ReservationResolveResult.Fail(
                ToolResultHelper.Error(
                    "reservation_not_accessible",
                    "This reservation does not belong to the current customer channel."));
        }

        if (reservation.Status is not (Domain.Enums.ReservationStatus.Confirmed or Domain.Enums.ReservationStatus.OnHold)
            || (reservation.ReservationDateTime.HasValue
                && DateOnly.FromDateTime(reservation.ReservationDateTime.Value) < ctx.BusinessToday))
        {
            return ReservationResolveResult.Fail(
                ToolResultHelper.Error(
                    "reservation_not_manageable",
                    "This reservation is not an upcoming manageable reservation.",
                    "Use get_customer_reservations to list current manageable reservations."));
        }

        return ReservationResolveResult.Ok(reservation);
    }

    private static bool PhoneMatches(string? snapshotPhone, string channelPhone)
    {
        if (string.IsNullOrWhiteSpace(snapshotPhone) || string.IsNullOrWhiteSpace(channelPhone))
            return false;

        return string.Equals(snapshotPhone.Trim(), channelPhone.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static ReservationResolveResult? ResolveFromList(IReadOnlyList<Domain.Entities.Reservation> reservations) =>
        reservations.Count switch
        {
            0 => null,
            1 => ReservationResolveResult.Ok(reservations[0]),
            _ => ReservationResolveResult.Fail(BuildMultipleReservationsError(reservations))
        };

    private static string BuildMultipleReservationsError(IReadOnlyList<Domain.Entities.Reservation> reservations)
    {
        var lines = reservations.Select(FormatReservationLine).ToList();
        var detail = string.Join("; ", lines);

        return ToolResultHelper.Error(
            "ambiguous_reservation",
            $"This customer has more than one upcoming reservation: {detail}.",
            "Ask which appointment they mean using date and service — never ask the customer for a UUID. Use reservation_id from tool context when retrying.");
    }

    internal static string FormatReservationLine(Domain.Entities.Reservation r)
    {
        var service = r.Service?.ServiceName ?? r.GetServiceName() ?? "servicio";
        if (!r.ReservationDateTime.HasValue)
            return $"{service} (id_reserva={r.ReservationId})";

        var date = DateOnly.FromDateTime(r.ReservationDateTime.Value).ToString("yyyy-MM-dd");
        var time = TimeOnly.FromDateTime(r.ReservationDateTime.Value).ToString("HH:mm");
        return $"{date} {time} {service} (id_reserva={r.ReservationId})";
    }
}
