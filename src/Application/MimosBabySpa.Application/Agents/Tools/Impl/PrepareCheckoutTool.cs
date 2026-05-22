using System.Text.Json;
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

    public string Name => "prepare_checkout";

    public string Description =>
        "Validates booking facts, resolves pricing, renders the checkout summary template, " +
        "and creates a payment link when the business policy requires a deposit. " +
        "Does not create a reservation. Returns pricing, flow metadata, and a rendered summary token.";

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
        if (ctx.Turn is null)
        {
            return ToolResultHelper.Error(
                "internal_error",
                "Turn context is not available for template rendering.");
        }

        var service = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service);
        var dateStr = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate);
        var timeStr = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime);
        var customerName = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerName)
            ?? ctx.Conversation.CustomerName;
        var customerPhone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone);
        var addOns = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(service)) missing.Add("service");
        if (string.IsNullOrWhiteSpace(dateStr)) missing.Add("desired_date");
        if (string.IsNullOrWhiteSpace(timeStr)) missing.Add("desired_time");
        if (string.IsNullOrWhiteSpace(customerName)) missing.Add("customer_name");
        if (string.IsNullOrWhiteSpace(customerPhone)) missing.Add("customer_phone");
        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        if (!AgentDateRules.TryParseDate(dateStr!, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        if (!string.IsNullOrWhiteSpace(addOns))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, service!, addOns, cancellationToken);
            if (!validation.IsValid)
            {
                return ToolResultHelper.Error(
                    "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Hint);
            }
        }

        var checkout = await _checkoutPricing.ResolveAsync(
            ctx.BusinessId, service!, addOns, cancellationToken);
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
            service!,
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
                ctx, intent, checkout, service!, customerPhone!, cancellationToken);
            if (linkResult.Error is not null)
                return linkResult.Error;

            linkUrl = linkResult.LinkUrl;
        }

        var templateId = checkout.DepositRequired
            ? "checkout_with_deposit"
            : "checkout_no_deposit";

        var templateData = CheckoutTemplateDataBuilder.Build(
            ctx, checkout, service!, date, time, customerName!, customerPhone!, linkUrl);

        var checkoutToken = ctx.Turn.RegisterFragment(
            "CHECKOUT", templateId, templateData, FragmentRenderMode.Exclusive);
        ctx.Turn.MarkCheckoutPrepared();

        var flow = checkout.DepositRequired ? "deposit_required" : "verbal_confirmation";
        var nextAction = checkout.DepositRequired
            ? "wait_for_customer_payment_confirmation"
            : "create_reservation_after_customer_says_yes";

        return ToolResultHelper.Ok(new
        {
            flow,
            checkout_token = checkoutToken,
            next_action_hint = nextAction,
            deposit_required = checkout.DepositRequired,
            is_booking_confirmed = false
        });
    }

    private async Task<(string? LinkUrl, string? Error)> EnsurePaymentLinkAsync(
        AgentToolContext ctx,
        ReservationIntentSnapshot intent,
        CheckoutPricingResult checkout,
        string service,
        string phone,
        CancellationToken cancellationToken)
    {
        var activePayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);

        if (activePayment?.LinkUrl is not null
            && activePayment.ExpiresAt.HasValue
            && activePayment.ExpiresAt.Value > DateTime.UtcNow
            && _paymentLifecycle.SnapshotsMatch(activePayment, intent, checkout.DepositCents))
        {
            ctx.ActivePayment = activePayment;
            return (activePayment.LinkUrl, null);
        }

        PaymentTransaction? supersededPayment = null;
        if (activePayment is not null && activePayment.Status == PaymentTransactionStatus.Created)
            supersededPayment = activePayment;

        var description = checkout.BuildServiceDescription();
        var currency = string.IsNullOrWhiteSpace(checkout.Policy.Currency) ? "COP" : checkout.Policy.Currency;

        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId,
                ctx.ConversationId,
                phone,
                description,
                checkout.DepositCents,
                currency,
                ExpirationMinutes: 60),
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
            result.ExpiresAt ?? DateTime.UtcNow.AddHours(1),
            cancellationToken);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, cancellationToken);

        ctx.ActivePayment = payment;
        return (payment.LinkUrl, null);
    }
}
