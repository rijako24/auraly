using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations.Support;
using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using ReservationEntity = MimosBabySpa.Domain.Entities.Reservation;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

public sealed class RequestRescheduleOperation : IAgentOperation
{
private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationLifecycleService _conversationLifecycle;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;

    public RequestRescheduleOperation(
        IUnitOfWork unitOfWork,
        IConversationLifecycleService conversationLifecycle,
        IOutboundMessageDispatcher outboundDispatcher)
    {
        _unitOfWork = unitOfWork;
        _conversationLifecycle = conversationLifecycle;
        _outboundDispatcher = outboundDispatcher;
    }

    public OperationDescriptor Descriptor => new(Name, ParametersSchema, ["internal.reschedule_requested"], [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement arguments, OperationContext context, CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("internal.request_reschedule requires a conversation session.");
        var json = await ExecuteCoreAsync(arguments, session, cancellationToken);
        return OperationJsonResult.Parse(json, "internal.reschedule_requested");
    }

    public string Name => "internal.request_reschedule";

    public string Description =>
        "Requests customers to reschedule affected reservations by sending them a WhatsApp message. " +
        "It does not move reservations directly; customer replies continue through the normal bot flow.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "YYYY-MM-DD, today/hoy, or tomorrow/manana" },
            "end_date": { "type": "string", "description": "Optional YYYY-MM-DD end date, max 7 days" },
            "reservation_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional explicit reservation UUIDs. If omitted, date is required."
            },
            "reason": { "type": "string", "description": "Short reason shown to customers. Defaults to 'se presento un inconveniente'." },
            "preview_only": { "type": "boolean" }
          }
        }
        """;

    private async Task<string> ExecuteCoreAsync(JsonElement arguments, AgentConversationContext ctx, CancellationToken cancellationToken = default)
    {
        OperationJsonHelper.TryGetBool(arguments, "preview_only", out var previewOnly);
        OperationJsonHelper.TryGetString(arguments, "reason", out var reason);
        reason = string.IsNullOrWhiteSpace(reason) ? "se presento un inconveniente" : reason.Trim();

        var reservationsResult = await ResolveReservationsAsync(arguments, ctx, cancellationToken);
        if (reservationsResult.ErrorJson is not null)
            return reservationsResult.ErrorJson;

        var reservations = reservationsResult.Reservations!
            .Where(IsRescheduleRequestable)
            .OrderBy(r => r.ReservationDateTime)
            .ToList();

        var skipped = reservationsResult.Reservations!
            .Where(r => !IsRescheduleRequestable(r))
            .Select(r => new
            {
                reservation_id = r.ReservationId,
                status = r.Status.ToString(),
                reason = "reservation_not_confirmed_or_on_hold"
            })
            .Cast<object>()
            .ToList();

        var groups = reservations
            .Where(r => !string.IsNullOrWhiteSpace(r.CustomerPhoneSnapshot))
            .GroupBy(r => InternalOperationParsing.NormalizePhone(r.CustomerPhoneSnapshot))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        skipped.AddRange(reservations
            .Where(r => string.IsNullOrWhiteSpace(r.CustomerPhoneSnapshot)
                || string.IsNullOrWhiteSpace(InternalOperationParsing.NormalizePhone(r.CustomerPhoneSnapshot)))
            .Select(r => new
            {
                reservation_id = r.ReservationId,
                status = r.Status.ToString(),
                reason = "missing_customer_phone"
            })
            .Cast<object>());

        var deliveries = new List<object>();
        var notifiedReservationIds = new List<Guid>();

        foreach (var group in groups)
        {
            var groupReservations = group.ToList();
            var phone = group.Key;
            var customerName = groupReservations.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.CustomerNameSnapshot))
                ?.CustomerNameSnapshot;
            var message = BuildMessage(customerName, groupReservations, reason);

            if (!previewOnly)
            {
                var conversation = await _conversationLifecycle.GetOrOpenForCustomerAsync(
                    ctx.BusinessId,
                    phone,
                    customerName,
                    cancellationToken);

                await _outboundDispatcher.SendAllAsync(
                    ctx.BusinessId,
                    phone,
                    [new OutboundMessage(message, null)],
                    conversation.ConversationId,
                    cancellationToken,
                    throwOnFailure: true);

                await _conversationLifecycle.TouchActivityAsync(
                    conversation.ConversationId,
                    message,
                    cancellationToken);

                foreach (var reservation in groupReservations.Where(r => r.Status == ReservationStatus.Confirmed))
                {
                    reservation.Status = ReservationStatus.OnHold;
                    reservation.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.Reservations.UpdateAsync(reservation);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            notifiedReservationIds.AddRange(groupReservations.Select(r => r.ReservationId));
            deliveries.Add(new
            {
                phone,
                customer_name = customerName,
                reservation_ids = groupReservations.Select(r => r.ReservationId),
                message
            });
        }

        var data = new
        {
            preview_only = previewOnly,
            sent = !previewOnly,
            notified_customers = deliveries.Count,
            notified_reservations = notifiedReservationIds.Count,
            reservation_ids = notifiedReservationIds,
            deliveries,
            skipped
        };

        return previewOnly
            ? OperationJsonHelper.Ok(data)
            : OperationJsonHelper.Ok(data, OperationEffectNames.RequestCompleted);
    }

    private async Task<(IReadOnlyList<ReservationEntity>? Reservations, string? ErrorJson)> ResolveReservationsAsync(
        JsonElement arguments,
        AgentConversationContext ctx,
        CancellationToken ct)
    {
        if (arguments.TryGetProperty("reservation_ids", out var idsElement)
            && idsElement.ValueKind == JsonValueKind.Array
            && idsElement.GetArrayLength() > 0)
        {
            var reservations = new List<ReservationEntity>();
            foreach (var idElement in idsElement.EnumerateArray())
            {
                if (idElement.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(idElement.GetString(), out var reservationId))
                {
                    return (null, OperationJsonHelper.Error("invalid_reservation_id", "reservation_ids must contain valid UUIDs."));
                }

                var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
                if (reservation is null || reservation.BusinessId != ctx.BusinessId)
                    return (null, OperationJsonHelper.Error("reservation_not_found", $"Reservation {reservationId} was not found in this business."));

                reservations.Add(reservation);
            }

            return (reservations, null);
        }

        if (!InternalOperationParsing.TryGetDate(arguments, "date", ctx.BusinessToday, out var startDate))
            return (null, OperationJsonHelper.Error("date_required", "date is required when reservation_ids are not provided."));

        var endDate = InternalOperationParsing.TryGetDate(arguments, "end_date", ctx.BusinessToday, out var parsedEnd)
            ? parsedEnd
            : startDate;

        if (endDate < startDate)
            return (null, OperationJsonHelper.Error("invalid_date_range", "end_date must be the same as or after date."));

        if (endDate.DayNumber - startDate.DayNumber > 7)
            return (null, OperationJsonHelper.Error("date_range_too_large", "Reschedule requests are limited to 7 days at a time."));

        var reservationsByDate = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            ctx.BusinessId,
            InternalOperationParsing.StartOfDay(startDate),
            InternalOperationParsing.EndOfDayInclusive(endDate));

        return (reservationsByDate.ToList(), null);
    }

    private static bool IsRescheduleRequestable(ReservationEntity reservation) =>
        reservation.Status is ReservationStatus.Confirmed or ReservationStatus.OnHold
        && reservation.ReservationDateTime.HasValue;

    private static string BuildMessage(string? customerName, IReadOnlyList<ReservationEntity> reservations, string reason)
    {
        var greeting = string.IsNullOrWhiteSpace(customerName)
            ? "Hola"
            : $"Hola {customerName.Trim()}";

        var lines = reservations
            .OrderBy(r => r.ReservationDateTime)
            .Select(r =>
            {
                var when = r.ReservationDateTime!.Value.ToString("yyyy-MM-dd HH:mm");
                var service = string.IsNullOrWhiteSpace(r.Service?.ServiceName)
                    ? "tu cita"
                    : r.Service.ServiceName;
                return $"- {when} - {service}";
            })
            .ToList();

        var appointmentText = lines.Count == 1
            ? $"tu cita de {lines[0].TrimStart('-', ' ')}"
            : $"estas citas:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";

        return $"{greeting}, {reason} y necesitamos reagendar {appointmentText}. " +
               "Para que dia y hora te gustaria reagendar? Si tienes varias citas, dime cual quieres mover primero.";
    }
}
