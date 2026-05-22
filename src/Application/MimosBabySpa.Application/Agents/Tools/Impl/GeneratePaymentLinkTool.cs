using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Legacy tool — prefer prepare_checkout. Generates payment link with snapshot, no draft reservation.
/// </summary>
public sealed class GeneratePaymentLinkTool : IAgentTool
{
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IReservationCheckoutPricing _checkoutPricing;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IEmployeeAssignmentService _employeeAssignment;

    public GeneratePaymentLinkTool(
        IPaymentLinkService paymentLinks,
        IReservationCheckoutPricing checkoutPricing,
        IPaymentLifecycleService paymentLifecycle,
        IReservationIntentBuilder intentBuilder,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IEmployeeAssignmentService employeeAssignment)
    {
        _paymentLinks = paymentLinks;
        _checkoutPricing = checkoutPricing;
        _paymentLifecycle = paymentLifecycle;
        _intentBuilder = intentBuilder;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _employeeAssignment = employeeAssignment;
    }

    public string Name => "generate_payment_link";

    public string Description =>
        "Creates a PaymentTransaction with an advance payment link and an immutable booking snapshot.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service_description": { "type": "string" }
          },
          "required": []
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var phone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone);
        if (string.IsNullOrWhiteSpace(phone))
            return ToolResultHelper.Error("missing_phone", "Contact phone is required before generating a payment link.");

        var service = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service);
        if (string.IsNullOrWhiteSpace(service))
            return ToolResultHelper.MissingPrerequisites(["service"]);

        var addOnsCsv = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns);
        var checkout = await _checkoutPricing.ResolveAsync(ctx.BusinessId, service, addOnsCsv, cancellationToken);

        if (checkout is null)
            return ToolResultHelper.Error("service_not_found", $"Service '{service}' was not found.");
        if (!checkout.DepositRequired)
            return ToolResultHelper.Error("deposit_not_required", "This business does not require an advance payment.");
        if (checkout.DepositCents <= 0)
            return ToolResultHelper.Error("invalid_deposit", "Deposit amount could not be calculated.");

        var intent = await _intentBuilder.BuildFromContextAsync(ctx, cancellationToken);
        if (intent is null)
            return ToolResultHelper.MissingPrerequisites(["desired_date", "desired_time"]);

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service,
            intent.ReservationDateTime.Date,
            intent.ReservationDateTime.TimeOfDay,
            policy,
            cancellationToken);

        if (!availability.IsAvailable)
            return ToolResultHelper.Error("slot_unavailable", availability.ResponseMessage ?? "Slot not available.");

        var endTime = intent.ReservationDateTime.AddMinutes(intent.DurationMinutes);
        var employee = await _employeeAssignment.FindBestAvailableEmployeeAsync(
            ctx.BusinessId, intent.ServiceId, intent.ReservationDateTime, endTime, cancellationToken);
        if (employee is null)
            return ToolResultHelper.Error("no_employee_available", "No staff available for this slot.");

        intent = intent with { PreferredEmployeeId = employee.EmployeeId };

        var activePayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);

        if (activePayment?.LinkUrl is not null
            && activePayment.ExpiresAt.HasValue
            && activePayment.ExpiresAt.Value > DateTime.UtcNow
            && activePayment.Snapshot_ServiceId == intent.ServiceId
            && activePayment.Snapshot_ReservationDateTime == intent.ReservationDateTime)
        {
            return ToolResultHelper.Ok(new
            {
                reused = true,
                payment_link_url = activePayment.LinkUrl,
                payment_reference_id = activePayment.PaymentReferenceId,
                deposit_cents = activePayment.AmountInCents,
                currency = activePayment.Currency,
                expires_at = activePayment.ExpiresAt
            });
        }

        var description = ToolResultHelper.TryGetString(arguments, "service_description", out var customDescription)
            && !string.IsNullOrWhiteSpace(customDescription)
                ? customDescription
                : checkout.BuildServiceDescription();

        var currency = string.IsNullOrWhiteSpace(checkout.Policy.Currency) ? "COP" : checkout.Policy.Currency;
        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId, ctx.ConversationId, phone,
                description, checkout.DepositCents, currency, ExpirationMinutes: 60),
            cancellationToken);

        if (!result.Success)
            return ToolResultHelper.Error("payment_link_failed", result.ErrorMessage ?? "Failed to generate payment link.");

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

        ctx.ActivePayment = payment;

        return ToolResultHelper.Ok(new
        {
            payment_link_url = payment.LinkUrl,
            payment_reference_id = payment.PaymentReferenceId,
            deposit_cents = payment.AmountInCents,
            currency = payment.Currency,
            expires_at = payment.ExpiresAt
        });
    }
}
