using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
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
    string TargetType,
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
        if (snapshot is null || !payment.Snapshot_ServiceId.HasValue)
            throw new InvalidOperationException("Intent de reserva incompleto.");

        var service = await _unitOfWork.Services.GetByIdAsync(payment.Snapshot_ServiceId.Value);
        if (service is null)
            throw new InvalidOperationException("Servicio del snapshot no encontrado.");

        snapshot = snapshot with { ServiceName = service.ServiceName };
        var businessId = state?.BusinessId ?? payment.BusinessId;
        var originalTime = TimeOnly.FromDateTime(snapshot.ReservationDateTime).ToString("HH:mm", CultureInfo.InvariantCulture);
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
            _logger.LogWarning("Webhook: slot taken after payment Ref={Ref}", payment.PaymentReferenceId);
            await _paymentLifecycle.MarkRequiresReschedulingAsync(payment, ct);

            var stubReservation = PaymentTransactionSnapshotMapper.ToNotificationReservation(payment, service.ServiceName);
            return new PaidCheckoutFulfillmentResult(
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                "reservation",
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
                    AvailableSlots = availability.AvailableTimeSlots
                });
        }

        Reservation reservation;
        try
        {
            var created = await _reservationService.CreateFromIntentSnapshotAsync(
                businessId,
                payment.ConversationId,
                snapshot,
                snapshot.ReservationDateTime,
                ct);

            await _paymentLifecycle.LinkReservationAsync(payment, created.ReservationId, ct);
            reservation = await _unitOfWork.Reservations.GetByIdAsync(created.ReservationId)
                ?? throw new InvalidOperationException("Reserva creada pero no encontrada.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook: CreateFromIntentSnapshot failed Ref={Ref}", payment.PaymentReferenceId);
            await _paymentLifecycle.MarkRequiresReschedulingAsync(payment, ct);

            var stubReservation = PaymentTransactionSnapshotMapper.ToNotificationReservation(payment, service.ServiceName);
            return new PaidCheckoutFulfillmentResult(
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                "reservation",
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
            outcome,
            "reservation",
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
            "enrollment",
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

    public OrderPaidCheckoutFulfillmentHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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
                ct)
            ?? throw new InvalidOperationException("Order not found for paid checkout.");
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
            outcome,
            "order",
            order.OrderId,
            order.CustomerPhoneSnapshot,
            custom,
            "payment_order_confirmed",
            NotifyAdmin: true,
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
