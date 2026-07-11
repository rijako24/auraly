using System.Globalization;
using System.Text;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class CustomerReservationResolver : ICustomerReservationResolver
{
    private static readonly string[] SpanishMonthNames =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

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
        AgentConversationContext ctx,
        string? reservationIdFromArgs,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(reservationIdFromArgs)
            && Guid.TryParse(reservationIdFromArgs, out var explicitId))
        {
            return await ResolveExplicitAsync(ctx, explicitId, ct);
        }

        var resolvedFromContext = ResolveFromList(ctx.ManageableReservations, ctx.BusinessToday, ctx.LatestUserMessage);
        if (resolvedFromContext is not null)
            return resolvedFromContext;

        var session = await _reservationLifecycle.ResolveForSessionAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            ctx.ChannelPhone,
            ctx.BusinessToday,
            ct);

        var resolvedFromSession = ResolveFromList(session.ManageableReservations, ctx.BusinessToday, ctx.LatestUserMessage);
        if (resolvedFromSession is not null)
            return resolvedFromSession;

        return ReservationResolveResult.Fail(
            OperationJsonHelper.ErrorWithLlm("no_manageable_reservation", "No confirmed reservation was found for this customer.", new { next_action = "collect_booking_request_or_handoff" }, recoverable: true));
    }

    private async Task<ReservationResolveResult> ResolveExplicitAsync(
        AgentConversationContext ctx,
        Guid reservationId,
        CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation is null || reservation.BusinessId != ctx.BusinessId)
        {
            return ReservationResolveResult.Fail(
                OperationJsonHelper.Error("reservation_not_found", "Reservation was not found."));
        }

        if (!BelongsToConversationOrChannel(reservation, ctx))
        {
            return ReservationResolveResult.Fail(
                OperationJsonHelper.Error(
                    "reservation_not_accessible",
                    "This reservation does not belong to the current customer channel."));
        }

        if (!ReservationTemporalFormatter.IsManageableOnBusinessDay(reservation, ctx.BusinessToday))
        {
            return ReservationResolveResult.Fail(
                OperationJsonHelper.ErrorWithLlm("reservation_not_manageable", "This reservation is not an upcoming manageable reservation.", new { next_action = "get_customer_reservations" }, recoverable: true));
        }

        if (ctx.ManageableReservations.Count > 1
            && ctx.ManageableReservations.Any(r => r.ReservationId == reservation.ReservationId)
            && !MessageIdentifiesReservation(ctx.LatestUserMessage, reservation))
        {
            return ReservationResolveResult.Fail(BuildMultipleReservationsError(ctx.ManageableReservations, ctx.BusinessToday));
        }

        return ReservationResolveResult.Ok(reservation);
    }

    private static bool BelongsToConversationOrChannel(Reservation reservation, AgentConversationContext ctx) =>
        reservation.ConversationId == ctx.ConversationId
        || PhoneMatches(reservation.CustomerPhoneSnapshot, ctx.ChannelPhone);

    private static bool PhoneMatches(string? snapshotPhone, string channelPhone)
    {
        var left = NormalizePhone(snapshotPhone);
        var right = NormalizePhone(channelPhone);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.TrimStart('0');
    }

    private static ReservationResolveResult? ResolveFromList(
        IReadOnlyList<Reservation> reservations,
        DateOnly businessToday,
        string latestUserMessage)
    {
        return reservations.Count switch
        {
            0 => null,
            1 => ReservationResolveResult.Ok(reservations[0]),
            _ => ResolveFromMultiple(reservations, businessToday, latestUserMessage)
        };
    }

    private static ReservationResolveResult ResolveFromMultiple(
        IReadOnlyList<Reservation> reservations,
        DateOnly businessToday,
        string latestUserMessage)
    {
        var matched = reservations
            .Where(r => MessageIdentifiesReservation(latestUserMessage, r))
            .ToList();

        return matched.Count == 1
            ? ReservationResolveResult.Ok(matched[0])
            : ReservationResolveResult.Fail(BuildMultipleReservationsError(reservations, businessToday));
    }

    private static bool MessageIdentifiesReservation(string message, Reservation reservation)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return ExistingDateMatches(message, reservation)
            || ExistingTimeMatches(message, reservation)
            || ExistingServiceMatches(message, reservation);
    }

    private static bool ExistingDateMatches(string message, Reservation reservation)
    {
        if (!reservation.ReservationDateTime.HasValue)
            return false;

        var normalized = NormalizeText(message);
        var date = DateOnly.FromDateTime(reservation.ReservationDateTime.Value);
        var month = SpanishMonthNames[date.Month - 1];

        return normalized.Contains(date.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(date.ToString("yyyy/MM/dd"), StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{date.Day} de {month}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{date.Day:00} de {month}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExistingTimeMatches(string message, Reservation reservation)
    {
        if (!reservation.ReservationDateTime.HasValue)
            return false;

        var normalized = NormalizeText(message);
        var time = TimeOnly.FromDateTime(reservation.ReservationDateTime.Value);
        return normalized.Contains(time.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExistingServiceMatches(string message, Reservation reservation)
    {
        var service = reservation.Service?.ServiceName ?? reservation.GetServiceName();
        if (string.IsNullOrWhiteSpace(service))
            return false;

        var normalizedMessage = NormalizeText(message);
        var tokens = NormalizeText(service)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tokens.Count > 0 && tokens.All(t => normalizedMessage.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildMultipleReservationsError(
        IReadOnlyList<Reservation> reservations,
        DateOnly businessToday)
    {
        var options = reservations
            .Select(r => ReservationTemporalFormatter.FormatLine(r, businessToday))
            .ToList();

        return OperationJsonHelper.ErrorWithLlm("ambiguous_reservation", "This customer has more than one upcoming reservation.", new
            {
                next_action = "select_reservation",
                reservations = options
            }, recoverable: true);
    }

    internal static string FormatReservationLine(Reservation reservation, DateOnly businessToday) =>
        ReservationTemporalFormatter.FormatLine(reservation, businessToday);
}
