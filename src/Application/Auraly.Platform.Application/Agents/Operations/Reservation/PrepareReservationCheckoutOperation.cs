using System.Text.Json;
using Auraly.Platform.Application.Agents.Gating;
using Auraly.Platform.Application.Agents.Templates;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Agents.Operations.Reservation;

public static class ReservationCheckoutOutcomeCodes
{
    public const string Prepared = "checkout.prepared";
    public const string MissingPrerequisites = "input.missing_prerequisites";
    public const string ServiceNotFound = "catalog.service_not_found";
    public const string InvalidAddOns = "catalog.invalid_add_ons";
    public const string AvailabilityMissing = "availability.verification_missing";
    public const string AvailabilityStale = "availability.verification_stale";
    public const string PaymentPhoneMissing = "payment.phone_missing";
    public const string PaymentLinkFailed = "payment.link_generation_failed";
    public const string ManualPaymentFailed = "payment.manual_preparation_failed";
}

public sealed class PrepareReservationCheckoutOperation : IAgentOperation
{
    private readonly IReservationCheckoutPreparationService _checkout;

    public PrepareReservationCheckoutOperation(IReservationCheckoutPreparationService checkout)
    {
        _checkout = checkout;
    }

    public OperationDescriptor Descriptor { get; } = new(
        "reservation.prepare_checkout",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "service": { "type": "string" },
            "add_ons": { "type": ["string", "null"] },
            "payment_method": { "type": ["string", "null"] },
            "context": {
              "type": ["object", "null"],
              "additionalProperties": { "type": ["string", "null"] }
            }
          },
          "required": ["service"]
        }
        """,
        [
            ReservationCheckoutOutcomeCodes.Prepared,
            ReservationCheckoutOutcomeCodes.MissingPrerequisites,
            ReservationCheckoutOutcomeCodes.ServiceNotFound,
            ReservationCheckoutOutcomeCodes.InvalidAddOns,
            ReservationCheckoutOutcomeCodes.AvailabilityMissing,
            ReservationCheckoutOutcomeCodes.AvailabilityStale,
            ReservationCheckoutOutcomeCodes.PaymentPhoneMissing,
            ReservationCheckoutOutcomeCodes.PaymentLinkFailed,
            ReservationCheckoutOutcomeCodes.ManualPaymentFailed,
            "configuration.checkout_mode_missing",
            "pricing.unresolved",
            "input.invalid_reservation_date",
            "input.invalid_reservation_time",
            "checkout_payment_methods_missing",
            "invalid_payment_method",
            "checkout_payment_percentage_missing",
            "checkout_payment_percentage_invalid",
            "checkout_template_missing",
            "checkout_outcome_missing"
        ],
        ["checkout.prepare"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _checkout.PrepareAsync(
            new ReservationCheckoutPreparationRequest(
                context.BusinessId,
                context.ConversationId,
                context.Config,
                context.ConversationState,
                context.Facts,
                ReadString(input, "service"),
                input.TryGetProperty("add_ons", out _),
                ReadString(input, "add_ons"),
                ReadString(input, "payment_method")),
            cancellationToken);

        if (!result.Success || result.Quote is null)
        {
            return OperationOutcome.Fail(
                result.Code,
                result.Message ?? "Checkout could not be prepared.",
                result.Recoverable,
                RemediationSignal(result.Code),
                new
                {
                    missingPrerequisites = result.MissingPrerequisites,
                    availablePaymentMethods = result.AvailablePaymentMethods
                });
        }

        var effects = new List<OperationEffect>
        {
            new SaveVerificationEffect(
                VerificationFactTypes.CheckoutPrepared,
                result.VerificationDependencies,
                null)
        };
        if (result.Quote.CheckoutKind == CheckoutKind.Reservation
            && result.Quote.PayableCents <= 0)
        {
            effects.Add(new SaveVerificationEffect(
                VerificationFactTypes.CheckoutNoPaymentPrepared,
                result.VerificationDependencies,
                null));
        }

        return OperationOutcome.Ok(
            ReservationCheckoutOutcomeCodes.Prepared,
            new
            {
                checkoutKind = result.Quote.CheckoutKind.ToString(),
                templateId = result.Quote.TemplateId,
                paymentRequired = result.Quote.PayableCents > 0,
                paymentPendingManualConfirmation = result.Quote.RequiresManualConfirmation,
                paymentTransactionId = result.Payment?.PaymentTransactionId,
                paymentMethodFactKey = result.PaymentMethodFactKey,
                paymentMethodFactValue = result.PaymentMethodFactValue,
                isBookingConfirmed = false
            },
            [
                new OperationPresentation(
                    result.Quote.TemplateId,
                    result.TemplateData,
                    FragmentRenderMode.Exclusive,
                    FragmentPriority.Required)
            ],
            effects);
    }

    private static string? ReadString(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? RemediationSignal(string code) => code switch
    {
        ReservationCheckoutOutcomeCodes.MissingPrerequisites => "facts.collect_missing_checkout_data",
        ReservationCheckoutOutcomeCodes.ServiceNotFound => "catalog.service_mentioned",
        ReservationCheckoutOutcomeCodes.InvalidAddOns => "catalog.add_ons_mentioned",
        ReservationCheckoutOutcomeCodes.AvailabilityMissing or ReservationCheckoutOutcomeCodes.AvailabilityStale
            => "reservation.check_availability",
        ReservationCheckoutOutcomeCodes.PaymentPhoneMissing => "facts.collect_payment_phone",
        "invalid_payment_method" => "payment.method_mentioned",
        _ => null
    };
}
