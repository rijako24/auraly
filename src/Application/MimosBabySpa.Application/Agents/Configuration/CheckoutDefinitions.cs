using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class CheckoutDefinitions
{
    public string Currency { get; set; } = "COP";
    public Dictionary<string, CheckoutModeDefinition> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CheckoutModeDefinition? ResolveMode(string checkoutKind)
    {
        if (Modes.TryGetValue(checkoutKind, out var mode))
            return mode;

        var match = Modes.FirstOrDefault(kvp =>
            kvp.Key.Equals(checkoutKind, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match.Key) ? null : match.Value;
    }
}

public sealed class CheckoutModeDefinition
{
    public Dictionary<string, CheckoutPaymentMethodDefinition> PaymentMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public OrderCheckoutShippingDefinition Shipping { get; set; } = new();
    // Optional advanced overrides. Normal tenant seeds should rely on engine defaults by checkout mode.
    public Dictionary<string, string> RequiredFactRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SystemFactBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TemplateFactBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CheckoutPaymentDefinition
{
    public int? Percentage { get; set; }
}

public sealed class CheckoutPaymentMethodDefinition
{
    public string? Label { get; set; }
    public List<string> Aliases { get; set; } = [];
    public CheckoutPaymentDefinition? Payment { get; set; }
    public string? Template { get; set; }
    public string? ConfirmationOutcome { get; set; }
}

public sealed class OrderCheckoutShippingDefinition
{
    public bool Enabled { get; set; }
    public string? LocalCity { get; set; }
    public decimal LocalCost { get; set; }
    public decimal NationalCost { get; set; }
}

public sealed record CheckoutModeBindings(
    IReadOnlyDictionary<string, string> RequiredFactRoles,
    IReadOnlyDictionary<string, string> SystemFactBindings,
    IReadOnlyDictionary<string, string> TemplateFactBindings);

public static class CheckoutModeBindingDefaults
{
    public static CheckoutModeBindings Resolve(CheckoutKind checkoutKind, CheckoutModeDefinition mode)
    {
        var defaults = checkoutKind switch
        {
            CheckoutKind.Enrollment => EnrollmentDefaults(),
            CheckoutKind.Order => OrderDefaults(),
            _ => ReservationDefaults()
        };

        return new CheckoutModeBindings(
            Merge(defaults.RequiredFactRoles, mode.RequiredFactRoles),
            Merge(defaults.SystemFactBindings, mode.SystemFactBindings),
            Merge(defaults.TemplateFactBindings, mode.TemplateFactBindings));
    }

    private static CheckoutModeBindings ReservationDefaults() =>
        new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "booking.service",
                ["reservation_date"] = "booking.date",
                ["reservation_time"] = "booking.time",
                ["customer_name"] = "customer.name",
                ["payment_phone"] = "customer.phone"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["reservation_date"] = "booking.date",
                ["reservation_time"] = "booking.time",
                ["payer_name"] = "customer.name",
                ["payment_phone"] = "customer.phone",
                ["payer_email"] = "customer.email"
            },
            CommonTemplateBindings(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["date"] = "booking.date",
                ["date_formatted"] = "booking.date",
                ["time"] = "booking.time"
            }));

    private static CheckoutModeBindings EnrollmentDefaults() =>
        new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "booking.service",
                ["fixed_schedule"] = "checkout.fixed_schedule",
                ["customer_name"] = "customer.name",
                ["payment_phone"] = "customer.phone"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fixed_schedule"] = "checkout.fixed_schedule",
                ["payer_name"] = "customer.name",
                ["payment_phone"] = "customer.phone",
                ["payer_email"] = "customer.email"
            },
            CommonTemplateBindings(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fixed_schedule"] = "checkout.fixed_schedule"
            }));

    private static CheckoutModeBindings OrderDefaults() =>
        new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["city"] = "shipping.city",
                ["delivery_address"] = "shipping.address",
                ["payment_phone"] = "customer.phone"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["city"] = "shipping.city",
                ["delivery_address"] = "shipping.address",
                ["payer_name"] = "customer.name",
                ["payment_phone"] = "customer.phone",
                ["payer_email"] = "customer.email"
            },
            CommonTemplateBindings(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["city"] = "shipping.city",
                ["delivery_address"] = "shipping.address"
            }));

    private static Dictionary<string, string> CommonTemplateBindings(Dictionary<string, string> modeBindings)
    {
        modeBindings["customer_name"] = "customer.name";
        modeBindings["customer_phone"] = "customer.phone";
        modeBindings["baby_name"] = "baby.name";
        modeBindings["baby_age_months"] = "baby.age_months";
        modeBindings["baby_birth_date"] = "baby.birth_date";
        return modeBindings;
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0)
            return new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
            result[key] = value;
        return result;
    }
}

public sealed record CheckoutPaymentSelection(
    bool MissingPaymentMethod,
    CheckoutPaymentSelectionError? Error,
    string MethodKey,
    string MethodLabel,
    int? PaymentPercentage,
    long PayableCents,
    string TemplateId,
    string ConfirmationOutcome)
{
    public bool RequiresPayment => PayableCents > 0;
}

public sealed record CheckoutPaymentSelectionError(string Code, string Message, string? Hint = null, bool Recoverable = false);

public static class CheckoutPaymentSelectionResolver
{
    public static CheckoutPaymentSelection Resolve(
        CheckoutModeDefinition mode,
        string checkoutKind,
        long totalCents,
        string? rawPaymentMethod)
    {
        if (mode.PaymentMethods.Count == 0)
        {
            return Error(
                "checkout_payment_methods_missing",
                $"Checkout mode '{checkoutKind}' has no paymentMethods configured.",
                "Configure at least one payment method with its template.");
        }

        var configured = ResolveMethod(mode, rawPaymentMethod);
        if (configured is null)
        {
            if (string.IsNullOrWhiteSpace(rawPaymentMethod))
            {
                return mode.PaymentMethods.Count == 1
                    ? FromMethod(mode.PaymentMethods.First(), totalCents, checkoutKind)
                    : new CheckoutPaymentSelection(true, null, string.Empty, string.Empty, null, 0, string.Empty, string.Empty);
            }

            var options = DescribeConfiguredPaymentMethods(mode);
            return Error(
                "invalid_payment_method",
                "Payment method is not configured for this checkout mode.",
                string.IsNullOrWhiteSpace(options)
                    ? "Ask the customer for a configured payment method."
                    : $"Ask the customer to choose one of the configured payment methods: {options}.",
                recoverable: true);
        }

        return FromMethod(configured.Value, totalCents, checkoutKind);
    }

    private static CheckoutPaymentSelection FromMethod(
        KeyValuePair<string, CheckoutPaymentMethodDefinition> configured,
        long totalCents,
        string checkoutKind)
    {
        var key = configured.Key;
        var method = configured.Value;
        var label = PaymentMethodLabel(key, method);
        var percentage = method.Payment?.Percentage;

        if (method.Payment is not null)
        {
            if (percentage is null)
            {
                return Error(
                    "checkout_payment_percentage_missing",
                    $"Payment method '{key}' in checkout mode '{checkoutKind}' has payment configured without percentage.",
                    "Set payment.percentage or remove payment from the method.");
            }

            if (percentage <= 0 || percentage > 100)
            {
                return Error(
                    "checkout_payment_percentage_invalid",
                    $"Payment method '{key}' in checkout mode '{checkoutKind}' has invalid payment percentage.",
                    "Use a percentage between 1 and 100.");
            }
        }

        var payableCents = percentage is null ? 0 : totalCents * percentage.Value / 100;
        if (string.IsNullOrWhiteSpace(method.Template))
        {
            return Error(
                "checkout_template_missing",
                $"Payment method '{key}' in checkout mode '{checkoutKind}' has no template configured.",
                "Set template in the selected payment method.");
        }

        if (payableCents > 0 && string.IsNullOrWhiteSpace(method.ConfirmationOutcome))
        {
            return Error(
                "checkout_outcome_missing",
                $"Payment method '{key}' in checkout mode '{checkoutKind}' creates a payment link but has no confirmationOutcome.",
                "Set confirmationOutcome in the selected payment method.");
        }

        return new CheckoutPaymentSelection(
            false,
            null,
            key,
            label,
            percentage,
            payableCents,
            method.Template!.Trim(),
            payableCents > 0 ? method.ConfirmationOutcome!.Trim() : string.Empty);
    }

    private static KeyValuePair<string, CheckoutPaymentMethodDefinition>? ResolveMethod(
        CheckoutModeDefinition mode,
        string? rawPaymentMethod)
    {
        var normalizedInput = NormalizePaymentMethodToken(rawPaymentMethod);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return null;

        foreach (var method in mode.PaymentMethods)
        {
            if (normalizedInput.Equals(NormalizePaymentMethodToken(method.Key), StringComparison.OrdinalIgnoreCase)
                || normalizedInput.Equals(NormalizePaymentMethodToken(method.Value.Label), StringComparison.OrdinalIgnoreCase)
                || method.Value.Aliases.Any(alias => normalizedInput.Equals(NormalizePaymentMethodToken(alias), StringComparison.OrdinalIgnoreCase)))
            {
                return method;
            }
        }

        return null;
    }

    private static string DescribeConfiguredPaymentMethods(CheckoutModeDefinition mode) =>
        string.Join(", ", mode.PaymentMethods.Select(kvp => PaymentMethodLabel(kvp.Key, kvp.Value)));

    private static string PaymentMethodLabel(string key, CheckoutPaymentMethodDefinition method) =>
        string.IsNullOrWhiteSpace(method.Label) ? key : method.Label.Trim();

    private static CheckoutPaymentSelection Error(
        string code,
        string message,
        string? hint = null,
        bool recoverable = false) =>
        new(false, new CheckoutPaymentSelectionError(code, message, hint, recoverable), string.Empty, string.Empty, null, 0, string.Empty, string.Empty);

    private static string NormalizePaymentMethodToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
