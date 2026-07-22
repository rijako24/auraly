using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public interface IPaidCheckoutFulfillmentHandler
{
    CheckoutKind Kind { get; }

    Task<bool> IsFulfilledAsync(PaymentTransaction payment, CancellationToken ct = default);

    Task<PaidCheckoutFulfillmentResult> FulfillAsync(
        PaymentTransaction payment,
        ConversationState? state,
        AgentConfig config,
        CancellationToken ct = default);
}

public interface IPaidCheckoutFulfillmentRegistry
{
    IPaidCheckoutFulfillmentHandler Resolve(CheckoutKind kind);
}

public sealed class PaidCheckoutFulfillmentRegistry : IPaidCheckoutFulfillmentRegistry
{
    private readonly IReadOnlyDictionary<CheckoutKind, IPaidCheckoutFulfillmentHandler> _handlers;

    public PaidCheckoutFulfillmentRegistry(IEnumerable<IPaidCheckoutFulfillmentHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Kind);
    }

    public IPaidCheckoutFulfillmentHandler Resolve(CheckoutKind kind) =>
        _handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException($"No paid checkout fulfillment handler registered for {kind}.");
}

public sealed record PaidCheckoutFulfillmentResult(
    string OutcomeKey,
    string EventName,
    Guid TargetId,
    string? CustomerPhone,
    IReadOnlyDictionary<string, string> CustomPayload,
    string CompletionReason,
    Reservation? SequenceReservation = null,
    Reservation? ReservationNotification = null,
    PaymentSequenceContext? Payment = null,
    bool NotifyCustomer = true,
    bool NotifyAdmin = false,
    bool TriggerExternalEscalation = false);

public sealed class ReservationPaidCheckoutFulfillmentHandler : IPaidCheckoutFulfillmentHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationService _reservationService;
    private readonly IAvailabilityService _availabilityService;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly ILogger<ReservationPaidCheckoutFulfillmentHandler> _logger;

    public ReservationPaidCheckoutFulfillmentHandler(
        IUnitOfWork unitOfWork,
        IPaymentLifecycleService paymentLifecycle,
        IReservationService reservationService,
        IAvailabilityService availabilityService,
        ISchedulingPolicyProvider schedulingPolicy,
        ILogger<ReservationPaidCheckoutFulfillmentHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _paymentLifecycle = paymentLifecycle;
        _reservationService = reservationService;
        _availabilityService = availabilityService;
        _schedulingPolicy = schedulingPolicy;
        _logger = logger;
    }

    public CheckoutKind Kind => CheckoutKind.Reservation;

    public Task<bool> IsFulfilledAsync(PaymentTransaction payment, CancellationToken ct = default) =>
        Task.FromResult(payment.ReservationId.HasValue || payment.RequiresRescheduling);

    public async Task<PaidCheckoutFulfillmentResult> FulfillAsync(
        PaymentTransaction payment,
        ConversationState? state,
        AgentConfig config,
        CancellationToken ct = default)
    {
        var snapshot = PaymentTransactionSnapshotMapper.ToIntentSnapshot(payment);
        if (snapshot is null)
            throw new InvalidOperationException("Intent de reserva incompleto.");

        var service = await _unitOfWork.Services.GetByIdAsync(snapshot.ServiceId);
        if (service is null)
            throw new InvalidOperationException("Servicio del snapshot no encontrado.");

        snapshot = snapshot with { ServiceName = service.ServiceName };
        var businessId = state?.BusinessId ?? payment.BusinessId;
        var originalTime = TimeOnly.FromDateTime(snapshot.ReservationDateTime).ToString("HH:mm", CultureInfo.InvariantCulture);
        var confirmationChannel = ResolveConfirmationChannel(payment);

        _logger.LogInformation(
            "PaidCheckout reservation fulfillment started Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} BusinessId={BusinessId} ConversationId={ConversationId} Status={Status} Source={Source} ServiceId={ServiceId} Service={Service} ReservationDateTime={ReservationDateTime} CustomerPhone={CustomerPhone}",
            confirmationChannel,
            payment.PaymentTransactionId,
            payment.PaymentReferenceId,
            businessId,
            payment.ConversationId,
            payment.Status,
            payment.Source,
            snapshot.ServiceId,
            service.ServiceName,
            snapshot.ReservationDateTime,
            snapshot.CustomerPhone);

        var existingReservation = await FindExistingReservationForPaymentAsync(
            businessId,
            payment.ConversationId,
            snapshot.ServiceId,
            snapshot.ReservationDateTime,
            ct);
        if (existingReservation is not null)
        {
            await _paymentLifecycle.LinkReservationAsync(payment, existingReservation.ReservationId, ct);

            _logger.LogInformation(
                "PaidCheckout linked existing reservation Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} ReservationId={ReservationId} BusinessId={BusinessId} ConversationId={ConversationId} ServiceId={ServiceId} ReservationDateTime={ReservationDateTime}",
                confirmationChannel,
                payment.PaymentTransactionId,
                payment.PaymentReferenceId,
                existingReservation.ReservationId,
                businessId,
                payment.ConversationId,
                snapshot.ServiceId,
                snapshot.ReservationDateTime);

            return new PaidCheckoutFulfillmentResult(
                ResolveOutcome(payment, WompiWebhookOutcomes.ReservationCreated),
                "order_created",
                existingReservation.ReservationId,
                existingReservation.CustomerPhoneSnapshot,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "payment_reservation_already_created",
                SequenceReservation: existingReservation,
                NotifyCustomer: false);
        }
        var policy = await _schedulingPolicy.GetAsync(businessId, ct);
        var availability = await _availabilityService.CheckAvailabilityAsync(
            businessId,
            service.ServiceName,
            snapshot.ReservationDateTime.Date,
            snapshot.ReservationDateTime.TimeOfDay,
            policy,
            ct);

        if (!availability.IsAvailable)
        {
            var conflicts = await FindConflictingReservationsAsync(
                businessId,
                snapshot.ReservationDateTime,
                snapshot.DurationMinutes,
                ct);

            _logger.LogWarning(
                "PaidCheckout slot taken after payment Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} BusinessId={BusinessId} ConversationId={ConversationId} ServiceId={ServiceId} Service={Service} ReservationDateTime={ReservationDateTime} DurationMinutes={DurationMinutes} CustomerPhone={CustomerPhone} Conflicts={Conflicts}",
                confirmationChannel,
                payment.PaymentTransactionId,
                payment.PaymentReferenceId,
                businessId,
                payment.ConversationId,
                snapshot.ServiceId,
                service.ServiceName,
                snapshot.ReservationDateTime,
                snapshot.DurationMinutes,
                snapshot.CustomerPhone,
                conflicts);
            await _paymentLifecycle.MarkRequiresReschedulingAsync(payment, ct);

            var stubReservation = PaymentTransactionSnapshotMapper.ToNotificationReservation(payment, service.ServiceName);
            return new PaidCheckoutFulfillmentResult(
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                stubReservation.ReservationId,
                stubReservation.CustomerPhoneSnapshot,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "payment_reservation_requires_rescheduling",
                SequenceReservation: stubReservation,
                Payment: new PaymentSequenceContext
                {
                    Amount = payment.AmountInCents / 100m,
                    Currency = payment.Currency,
                    OriginalTime = originalTime,
                    AvailableSlots = availability.AvailableOptions.Select(o => $"{o.Start}-{o.End}").ToList()
                });
        }

        Reservation reservation;
        try
        {
            _logger.LogInformation(
                "PaidCheckout creating reservation from payment Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} BusinessId={BusinessId} ConversationId={ConversationId} ServiceId={ServiceId} Service={Service} ReservationDateTime={ReservationDateTime}",
                confirmationChannel,
                payment.PaymentTransactionId,
                payment.PaymentReferenceId,
                businessId,
                payment.ConversationId,
                snapshot.ServiceId,
                service.ServiceName,
                snapshot.ReservationDateTime);

            var created = await _reservationService.CreateFromIntentSnapshotAsync(
                businessId,
                payment.ConversationId,
                snapshot,
                snapshot.ReservationDateTime,
                ct);

            await _paymentLifecycle.LinkReservationAsync(payment, created.ReservationId, ct);
            reservation = await _unitOfWork.Reservations.GetByIdAsync(created.ReservationId)
                ?? throw new InvalidOperationException("Reserva creada pero no encontrada.");

            _logger.LogInformation(
                "PaidCheckout reservation created and linked Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} ReservationId={ReservationId} BusinessId={BusinessId} ConversationId={ConversationId}",
                confirmationChannel,
                payment.PaymentTransactionId,
                payment.PaymentReferenceId,
                reservation.ReservationId,
                businessId,
                payment.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PaidCheckout CreateFromIntentSnapshot failed Channel={Channel} PaymentTransactionId={PaymentTransactionId} Ref={Ref} BusinessId={BusinessId} ConversationId={ConversationId} ServiceId={ServiceId} Service={Service} ReservationDateTime={ReservationDateTime}",
                confirmationChannel,
                payment.PaymentTransactionId,
                payment.PaymentReferenceId,
                businessId,
                payment.ConversationId,
                snapshot.ServiceId,
                service.ServiceName,
                snapshot.ReservationDateTime);
            await _paymentLifecycle.MarkRequiresReschedulingAsync(payment, ct);

            var stubReservation = PaymentTransactionSnapshotMapper.ToNotificationReservation(payment, service.ServiceName);
            return new PaidCheckoutFulfillmentResult(
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                stubReservation.ReservationId,
                stubReservation.CustomerPhoneSnapshot,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "payment_reservation_requires_rescheduling",
                SequenceReservation: stubReservation,
                NotifyCustomer: false);
        }

        var outcome = ResolveOutcome(payment, WompiWebhookOutcomes.ReservationCreated);
        return new PaidCheckoutFulfillmentResult(
            outcome,
            "order_created",
            reservation.ReservationId,
            reservation.CustomerPhoneSnapshot,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "payment_reservation_confirmed",
            SequenceReservation: reservation,
            ReservationNotification: reservation);
    }

    private static string ResolveOutcome(PaymentTransaction payment, string legacyFallback) =>
        !string.IsNullOrWhiteSpace(payment.ConfirmationOutcome)
            ? payment.ConfirmationOutcome
            : legacyFallback;

    private async Task<string> FindConflictingReservationsAsync(
        Guid businessId,
        DateTime start,
        int durationMinutes,
        CancellationToken ct)
    {
        var end = start.AddMinutes(durationMinutes);
        var sameDay = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            start.Date,
            start.Date.AddDays(1).AddTicks(-1));

        var conflicts = sameDay
            .Where(r => r.ReservationDateTime.HasValue)
            .Where(r => r.Status is ReservationStatus.Confirmed
                or ReservationStatus.Completed
                or ReservationStatus.OnHold
                or ReservationStatus.PendingCalendar)
            .Where(r =>
            {
                var conflictStart = r.ReservationDateTime!.Value;
                var conflictEnd = conflictStart.AddMinutes(r.DurationMinutes ?? durationMinutes);
                return conflictStart < end && conflictEnd > start;
            })
            .Select(r => string.Join("|", new[]
            {
                $"reservation_id={r.ReservationId}",
                $"conversation_id={r.ConversationId}",
                $"status={r.Status}",
                $"service_id={r.ServiceId}",
                $"service={r.Service?.ServiceName ?? string.Empty}",
                $"start={r.ReservationDateTime:O}",
                $"duration={r.DurationMinutes}",
                $"created_at={r.CreatedAt:O}",
                $"updated_at={r.UpdatedAt:O}",
                $"customer_phone={r.CustomerPhoneSnapshot ?? string.Empty}"
            }))
            .ToList();

        return conflicts.Count == 0 ? "(none_found)" : string.Join("; ", conflicts);
    }

    private async Task<Reservation?> FindExistingReservationForPaymentAsync(
        Guid businessId,
        Guid conversationId,
        Guid serviceId,
        DateTime reservationDateTime,
        CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetActiveByConversationIdAsync(conversationId, ct);
        if (reservation is null)
            return null;

        if (reservation.BusinessId != businessId
            || reservation.ServiceId != serviceId
            || reservation.ReservationDateTime != reservationDateTime)
        {
            return null;
        }

        return reservation.Status is ReservationStatus.Confirmed
            or ReservationStatus.PendingCalendar
            or ReservationStatus.OnHold
            ? reservation
            : null;
    }
    private static string ResolveConfirmationChannel(PaymentTransaction payment)
    {
        if (payment.Source == PaymentTransactionSource.Manual)
            return "manual_confirmation";

        var payload = payment.WebhookPayloadJson;
        if (!string.IsNullOrWhiteSpace(payload)
            && payload.TrimStart().StartsWith("[Poller ", StringComparison.OrdinalIgnoreCase))
        {
            return "payment_link_polling";
        }

        return "wompi_webhook";
    }
}

public sealed class EnrollmentPaidCheckoutFulfillmentHandler : IPaidCheckoutFulfillmentHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public EnrollmentPaidCheckoutFulfillmentHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public CheckoutKind Kind => CheckoutKind.Enrollment;

    public async Task<bool> IsFulfilledAsync(PaymentTransaction payment, CancellationToken ct = default) =>
        await _unitOfWork.Enrollments.GetByPaymentTransactionIdAsync(payment.PaymentTransactionId, ct) is not null;

    public async Task<PaidCheckoutFulfillmentResult> FulfillAsync(
        PaymentTransaction payment,
        ConversationState? state,
        AgentConfig config,
        CancellationToken ct = default)
    {
        var snapshot = ParseCheckoutSnapshot(payment.CheckoutSnapshotJson)
            ?? throw new InvalidOperationException("Enrollment checkout snapshot is missing or invalid.");

        var existing = await _unitOfWork.Enrollments.GetByPaymentTransactionIdAsync(payment.PaymentTransactionId, ct);
        if (existing is null)
        {
            await _unitOfWork.Enrollments.CreateAsync(new Enrollment
            {
                EnrollmentId = Guid.NewGuid(),
                BusinessId = payment.BusinessId,
                ConversationId = payment.ConversationId,
                ServiceId = snapshot.ServiceId,
                PaymentTransactionId = payment.PaymentTransactionId,
                CustomerName = snapshot.PayerName ?? string.Empty,
                CustomerPhone = snapshot.PaymentPhone ?? string.Empty,
                CustomerEmail = snapshot.PayerEmail,
                FixedScheduleLabel = snapshot.FixedSchedule,
                Status = EnrollmentStatus.Paid,
                CustomAttributesJson = payment.CheckoutSnapshotJson,
                CreatedAt = DateTime.UtcNow
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var notification = new Reservation
        {
            ReservationId = Guid.Empty,
            BusinessId = payment.BusinessId,
            ConversationId = payment.ConversationId,
            ServiceId = snapshot.ServiceId,
            CustomerNameSnapshot = snapshot.PayerName,
            CustomerPhoneSnapshot = snapshot.PaymentPhone,
            CustomerEmailSnapshot = snapshot.PayerEmail,
            CustomAttributesJson = payment.CheckoutSnapshotJson,
            Service = new Service { ServiceId = snapshot.ServiceId, ServiceName = snapshot.ServiceName ?? string.Empty }
        };

        var custom = new Dictionary<string, string>(snapshot.Facts ?? [], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(snapshot.FixedSchedule))
            custom["fixed_schedule"] = snapshot.FixedSchedule;
        if (!string.IsNullOrWhiteSpace(snapshot.ServiceCategory))
            custom["service_category"] = snapshot.ServiceCategory;

        return new PaidCheckoutFulfillmentResult(
            ResolveOutcome(payment, string.Empty),
            ResolveOutcome(payment, string.Empty),
            payment.PaymentTransactionId,
            snapshot.PaymentPhone,
            custom,
            "payment_enrollment_confirmed",
            SequenceReservation: notification,
            Payment: new PaymentSequenceContext
            {
                Amount = payment.AmountInCents / 100m,
                Currency = payment.Currency
            });
    }

    private static string ResolveOutcome(PaymentTransaction payment, string legacyFallback) =>
        !string.IsNullOrWhiteSpace(payment.ConfirmationOutcome)
            ? payment.ConfirmationOutcome
            : legacyFallback;

    private static GenericCheckoutSnapshot? ParseCheckoutSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GenericCheckoutSnapshot>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class GenericCheckoutSnapshot
    {
        [JsonPropertyName("service_id")]
        public Guid ServiceId { get; set; }

        [JsonPropertyName("service_name")]
        public string? ServiceName { get; set; }

        [JsonPropertyName("service_category")]
        public string? ServiceCategory { get; set; }

        [JsonPropertyName("payer_name")]
        public string? PayerName { get; set; }

        [JsonPropertyName("payment_phone")]
        public string? PaymentPhone { get; set; }

        [JsonPropertyName("payer_email")]
        public string? PayerEmail { get; set; }

        [JsonPropertyName("fixed_schedule")]
        public string? FixedSchedule { get; set; }

        [JsonPropertyName("facts")]
        public Dictionary<string, string>? Facts { get; set; }
    }
}

public sealed class OrderPaidCheckoutFulfillmentHandler : IPaidCheckoutFulfillmentHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommerceService _commerce;

    public OrderPaidCheckoutFulfillmentHandler(IUnitOfWork unitOfWork, ICommerceService commerce)
    {
        _unitOfWork = unitOfWork;
        _commerce = commerce;
    }

    public CheckoutKind Kind => CheckoutKind.Order;

    public async Task<bool> IsFulfilledAsync(PaymentTransaction payment, CancellationToken ct = default)
    {
        var order = await _unitOfWork.Orders.GetByPaymentTransactionIdAsync(
            payment.BusinessId,
            payment.PaymentTransactionId,
            ct);
        return order?.Status is OrderStatus.Confirmed or OrderStatus.SyncPending or OrderStatus.Synced;
    }

    public async Task<PaidCheckoutFulfillmentResult> FulfillAsync(
        PaymentTransaction payment,
        ConversationState? state,
        AgentConfig config,
        CancellationToken ct = default)
    {
        var order = await _unitOfWork.Orders.GetByPaymentTransactionIdAsync(
            payment.BusinessId,
            payment.PaymentTransactionId,
            ct);
        if (order is null)
        {
            await _commerce.ConfirmPaidOrderAsync(payment.BusinessId, payment.PaymentTransactionId, config, ct);
            order = await _unitOfWork.Orders.GetByPaymentTransactionIdAsync(
                payment.BusinessId,
                payment.PaymentTransactionId,
                ct)
                ?? throw new InvalidOperationException("Order not found for paid checkout.");
        }

        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(payment.BusinessId, order.OrderId, ct);
        if (items.Count == 0)
            throw new InvalidOperationException("Paid order has no items.");

        order.CustomerConfirmed = true;
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Orders.UpdateAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var custom = BuildCustomPayload(order, items, payment);
        var outcome = ResolveOutcome(payment, "order_paid");

        return new PaidCheckoutFulfillmentResult(
            outcome,
            "order_created",
            order.OrderId,
            order.CustomerPhoneSnapshot,
            custom,
            "payment_order_confirmed",
            NotifyAdmin: true,
            NotifyCustomer: false,
            TriggerExternalEscalation: true);
    }

    private static Dictionary<string, string> BuildCustomPayload(
        Order order,
        IReadOnlyList<OrderItem> items,
        PaymentTransaction payment)
    {
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = order.OrderId.ToString(),
            ["order_number"] = ShortId(order.OrderId),
            ["customer_name"] = order.CustomerNameSnapshot ?? string.Empty,
            ["customer_phone"] = order.CustomerPhoneSnapshot ?? string.Empty,
            ["customer_email"] = order.CustomerEmailSnapshot ?? string.Empty,
            ["delivery_address"] = order.DeliveryAddressSnapshot ?? string.Empty,
            ["notes"] = order.Notes ?? string.Empty,
            ["currency"] = order.Currency,
            ["subtotal"] = Money(order.Subtotal),
            ["total"] = Money(order.Total),
            ["paid_amount"] = Money(payment.AmountInCents / 100m),
            ["payment_transaction_id"] = payment.PaymentTransactionId.ToString(),
            ["payment_reference_id"] = payment.PaymentReferenceId,
            ["items"] = string.Join("; ", items.Select(i => $"{i.ProductNameSnapshot} x{i.Quantity:N0}"))
        };

        TryReadOrderCustomAttributes(order.CustomAttributesJson, custom);
        return custom;
    }

    private static void TryReadOrderCustomAttributes(string? json, Dictionary<string, string> custom)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var name in new[] { "city", "shipping_cost" })
            {
                if (root.TryGetProperty(name, out var value))
                    custom[name] = value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.GetRawText();
            }

            if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object)
            {
                foreach (var fact in facts.EnumerateObject())
                {
                    if (fact.Value.ValueKind == JsonValueKind.String)
                        custom.TryAdd(fact.Name, fact.Value.GetString() ?? string.Empty);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string ResolveOutcome(PaymentTransaction payment, string legacyFallback) =>
        !string.IsNullOrWhiteSpace(payment.ConfirmationOutcome)
            ? payment.ConfirmationOutcome
            : legacyFallback;

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture);
}
