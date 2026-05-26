using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Prepara el cierre de venta: pricing, plantilla y link de pago con snapshot inmutable.
/// No crea reservas — solo PaymentTransactions cuando hay anticipo.
/// </summary>
public sealed class PrepareCheckoutTool : IAgentTool
{
    private readonly IReservationCheckoutPricing _checkoutPricing;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IEmployeeAssignmentService _employeeAssignment;

    public PrepareCheckoutTool(
        IReservationCheckoutPricing checkoutPricing,
        IAddOnCatalogService addOnCatalog,
        IPaymentLinkService paymentLinks,
        IPaymentLifecycleService paymentLifecycle,
        IReservationIntentBuilder intentBuilder,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment)
    {
        _checkoutPricing = checkoutPricing;
        _addOnCatalog = addOnCatalog;
        _paymentLinks = paymentLinks;
        _paymentLifecycle = paymentLifecycle;
        _intentBuilder = intentBuilder;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
    }

    public string PackId => BookingPackIds.Booking;

    public string Name => "prepare_checkout";

    public IReadOnlyList<RoleRequirement> RoleRequirements =>
    [
        new(FactRoles.BookingService),
        new(FactRoles.BookingDate),
        new(FactRoles.BookingTime),
        new(FactRoles.CustomerName),
        new(FactRoles.CustomerPhone),
        new(FactRoles.BookingAddOns, Required: false)
    ];

    public IReadOnlyList<string> RequiredTemplateIds => [];

    public string Description =>
        "Non-destructive step: validates booking facts, resolves final pricing, renders the checkout summary, " +
        "and generates a payment link only when a deposit is required by policy. " +
        "Call this proactively as soon as all booking facts (service, date, time, name, add_ons) are ready — " +
        "do NOT ask the customer for permission first. Does not create a reservation.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var ctx = invocation.Context;
        var booking = ctx.GetPackContext<IBookingPackContext>();
        var bookingPolicy = booking?.BookingPolicy;
        if (bookingPolicy is null || string.IsNullOrWhiteSpace(bookingPolicy.Currency))
        {
            return ToolResultHelper.Error(
                "missing_currency",
                "Booking policy currency is not configured for this business.");
        }

        var service = invocation.GetRequired(FactRoles.BookingService);
        var dateStr = invocation.GetRequired(FactRoles.BookingDate);
        var timeStr = invocation.GetRequired(FactRoles.BookingTime);
        var customerName = invocation.Get(FactRoles.CustomerName) ?? ctx.Conversation.CustomerName;
        var customerPhone = ConversationContactPhone.Resolve(ctx)
            ?? invocation.Get(FactRoles.CustomerPhone);
        var addOns = invocation.Get(FactRoles.BookingAddOns);

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
            return ToolResultHelper.MissingPrerequisites(["customer_name", "customer_phone"]);

        if (!AgentDateRules.TryParseDate(dateStr, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        if (!string.IsNullOrWhiteSpace(addOns))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, service, addOns, cancellationToken);
            if (!validation.IsValid)
            {
                return ToolResultHelper.Error(
                    "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Hint);
            }
        }

        var checkout = await _checkoutPricing.ResolveAsync(
            ctx.BusinessId, service, addOns, cancellationToken);
        if (checkout is null)
        {
            return ToolResultHelper.Error(
                "service_not_found",
                $"Service '{service}' was not found in the catalog.",
                "Call get_service_catalog to get the current list of services.");
        }

        var intent = await _intentBuilder.BuildFromContextAsync(ctx, cancellationToken);
        if (intent is null)
        {
            return ToolResultHelper.Error(
                "invalid_booking_data",
                "Could not build reservation intent from collected facts.");
        }

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service,
            date.ToDateTime(TimeOnly.MinValue),
            time.ToTimeSpan(),
            policy,
            cancellationToken);

        if (!availability.IsAvailable)
        {
            return ToolResultHelper.Error(
                "slot_unavailable",
                availability.ResponseMessage ?? "The selected time is not available.",
                availability.AvailableTimeSlots.Count > 0
                    ? $"Available slots: {string.Join(", ", availability.AvailableTimeSlots)}"
                    : null);
        }

        var endTime = intent.ReservationDateTime.AddMinutes(intent.DurationMinutes);
        var employee = await _employeeAssignment.FindBestAvailableEmployeeAsync(
            ctx.BusinessId,
            intent.ServiceId,
            intent.ReservationDateTime,
            endTime,
            cancellationToken);

        if (employee is null)
        {
            return ToolResultHelper.Error(
                "no_employee_available",
                "No staff member is available for this time slot.",
                "Offer alternative times via check_availability.");
        }

        intent = intent with { PreferredEmployeeId = employee.EmployeeId };

        string? linkUrl = null;

        if (checkout.DepositRequired)
        {
            var linkResult = await EnsurePaymentLinkAsync(
                ctx, booking, bookingPolicy, intent, checkout, service, customerPhone, cancellationToken);
            if (linkResult.Error is not null)
                return linkResult.Error;

            linkUrl = linkResult.LinkUrl;
        }

        var templateId = checkout.DepositRequired
            ? "checkout_with_deposit"
            : "checkout_no_deposit";

        var templateData = CheckoutTemplateDataBuilder.Build(
            ctx, checkout, service, date, time, customerName, customerPhone, linkUrl);

        var flow = checkout.DepositRequired ? "deposit_required" : "verbal_confirmation";

        return ToolResultHelper.Ok(new
        {
            flow,
            template_id = templateId,
            template_data = templateData,
            deposit_required = checkout.DepositRequired,
            is_booking_confirmed = false
        });
    }

    private async Task<(string? LinkUrl, string? Error)> EnsurePaymentLinkAsync(
        AgentToolContext ctx,
        IBookingPackContext? booking,
        BookingPolicyParams bookingPolicy,
        ReservationIntentSnapshot intent,
        CheckoutPricingResult checkout,
        string service,
        string phone,
        CancellationToken cancellationToken)
    {
        var activePayment = booking?.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);

        if (activePayment?.LinkUrl is not null
            && activePayment.ExpiresAt.HasValue
            && activePayment.ExpiresAt.Value > DateTime.UtcNow
            && _paymentLifecycle.SnapshotsMatch(activePayment, intent, checkout.DepositCents))
        {
            BookingPackContext.Replace(ctx, activePayment: activePayment);
            return (activePayment.LinkUrl, null);
        }

        PaymentTransaction? supersededPayment = null;
        if (activePayment is not null && activePayment.Status == PaymentTransactionStatus.Created)
            supersededPayment = activePayment;

        var description = checkout.BuildServiceDescription();
        var currency = bookingPolicy.Currency;

        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId,
                ctx.ConversationId,
                phone,
                description,
                checkout.DepositCents,
                currency,
                ExpirationMinutes: bookingPolicy.PaymentLinkExpirationMinutes),
            cancellationToken);

        if (!result.Success)
        {
            return (null, ToolResultHelper.Error(
                "payment_link_failed",
                result.ErrorMessage ?? "Failed to generate payment link."));
        }

        var payment = await _paymentLifecycle.CreatePendingAsync(
            ctx.BusinessId,
            ctx.ConversationId,
            intent,
            result.PaymentReferenceId!,
            result.PaymentLinkUrl!,
            checkout.DepositCents,
            currency,
            result.ExpiresAt ?? DateTime.UtcNow.AddMinutes(bookingPolicy.PaymentLinkExpirationMinutes),
            cancellationToken);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, cancellationToken);

        BookingPackContext.Replace(ctx, activePayment: payment);
        return (payment.LinkUrl, null);
    }
}
