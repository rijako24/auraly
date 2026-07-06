using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Agenda una reserva pagada que quedó pendiente porque el horario original se ocupó después del pago.
/// </summary>
[AgentToolMetadata("reschedule_paid_reservation", Capabilities = new[] { ToolCapabilities.PaidReservationReschedule })]
public sealed class ReschedulePaidReservationTool : IAgentTool
{
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationService _reservations;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationVerificationService _verifications;

    public ReschedulePaidReservationTool(
        IPaymentLifecycleService paymentLifecycle,
        IReservationService reservations,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IUnitOfWork unitOfWork,
        IConversationVerificationService verifications)
    {
        _paymentLifecycle = paymentLifecycle;
        _reservations = reservations;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _unitOfWork = unitOfWork;
        _verifications = verifications;
    }

    public string Name => "reschedule_paid_reservation";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.PaidReservationReschedule];

    public string Description =>
        "Creates a confirmed reservation for a paid PaymentTransaction that requires rescheduling and has no linked reservation yet, " +
        "using the verified service/date/time snapshot. Links the reservation to the payment record.";

    public Func<JsonElement, AgentToolContext, IReadOnlyDictionary<string, string>?>? VerificationDependencyResolver =>
        (args, ctx) =>
        {
            if (!ToolResultHelper.TryGetString(args, "date", out var date) || string.IsNullOrWhiteSpace(date))
                return null;
            if (!ToolResultHelper.TryGetString(args, "time", out var time) || string.IsNullOrWhiteSpace(time))
                return null;

            var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
            var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
            var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
            var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

            ctx.Facts.TryGetValue(serviceKey, out var service);
            if (string.IsNullOrWhiteSpace(service))
                return null;

            return VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(serviceKey, service),
                new KeyValuePair<string, string>(dateKey, date),
                new KeyValuePair<string, string>(timeKey, time));
        };

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "payment_transaction_id": { "type": "string", "description": "Optional GUID; defaults to pending reschedule payment for this conversation" },
            "date": { "type": "string", "description": "New date YYYY-MM-DD" },
            "time": { "type": "string", "description": "New time HH:mm" }
          },
          "required": ["date", "time"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        PaymentTransaction? payment = null;

        if (ToolResultHelper.TryGetString(arguments, "payment_transaction_id", out var paymentIdStr)
            && Guid.TryParse(paymentIdStr, out var paymentId))
        {
            payment = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentId, cancellationToken);
        }

        payment ??= ctx.ActivePayment;
        payment ??= await _paymentLifecycle.GetPendingReschedulingByConversationAsync(ctx.ConversationId, cancellationToken);

        if (payment is null
            || payment.Status != PaymentTransactionStatus.Confirmed
            || !payment.RequiresRescheduling)
        {
            return ToolResultHelper.Error(
                "no_pending_reschedule",
                "There is no confirmed paid reservation pending rescheduling for this conversation.");
        }

        if (payment.ReservationId.HasValue)
        {
            return ToolResultHelper.Ok(new
            {
                reservation_id = payment.ReservationId.Value,
                payment_transaction_id = payment.PaymentTransactionId,
                status = ReservationStatus.Confirmed.ToString(),
                is_booking_confirmed = true,
                idempotent_replay = true
            });
        }

        var dateStr = Coalesce(arguments, "date", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate));
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));

        if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(timeStr))
            return ToolResultHelper.MissingPrerequisites(["date", "time"]);

        if (!AgentDateRules.TryParseDate(dateStr!, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        var snapshot = PaymentTransactionSnapshotMapper.ToIntentSnapshot(payment);
        if (snapshot is null)
        {
            return ToolResultHelper.Error(
                "invalid_payment_snapshot",
                "Payment transaction is missing reservation snapshot data.");
        }

        var service = await _unitOfWork.Services.GetByIdAsync(snapshot.ServiceId);
        if (service is null)
            return ToolResultHelper.Error("service_not_found", "Snapshot service no longer exists.");

        snapshot = snapshot with { ServiceName = service.ServiceName };

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service.ServiceName,
            date.ToDateTime(TimeOnly.MinValue),
            time.ToTimeSpan(),
            policy,
            cancellationToken);

        if (!availability.IsAvailable)
        {
            return ToolResultHelper.Error(
                "slot_unavailable",
                availability.ResponseMessage ?? "The selected time is not available.",
                availability.AvailableOptions.Count > 0
                    ? $"Available options: {string.Join(", ", availability.AvailableOptions.Select(o => $"{o.Start}-{o.End}"))}"
                    : null);
        }

        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(serviceKey, service.ServiceName),
                new KeyValuePair<string, string>(dateKey, dateStr!),
                new KeyValuePair<string, string>(timeKey, timeStr!)),
            VerificationTtl.AvailabilityChecked);

        var newDateTime = date.ToDateTime(time);

        CreateReservationResponse response;
        try
        {
            response = await _reservations.CreateFromIntentSnapshotAsync(
                ctx.BusinessId,
                ctx.ConversationId,
                snapshot,
                newDateTime,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error("no_employee_available", ex.Message);
        }

        await _paymentLifecycle.LinkReservationAsync(payment, response.ReservationId, cancellationToken);

        var reservation = new Reservation
        {
            ReservationId = response.ReservationId,
            BusinessId = ctx.BusinessId,
            ServiceId = snapshot.ServiceId,
            Service = service,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = newDateTime,
            DurationMinutes = snapshot.DurationMinutes,
            CustomerNameSnapshot = snapshot.CustomerName,
            CustomerEmailSnapshot = snapshot.CustomerEmail,
            CustomerPhoneSnapshot = snapshot.CustomerPhone,
            CustomAttributesJson = snapshot.CustomAttributesJson
        };

        ctx.ManageableReservations = [reservation];
        ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext { Reservation = reservation };

        return ToolResultHelper.OkWithEvents(new
        {
            reservation_id = response.ReservationId,
            payment_transaction_id = payment.PaymentTransactionId,
            service = response.ServiceName,
            date = dateStr,
            time = timeStr,
            customer_name = snapshot.CustomerName,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true
        }, [ToolSideEffectNames.RequestCompleted], ["reservation_created"]);
    }

    private static string? Coalesce(JsonElement args, string property, string? fallback)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
