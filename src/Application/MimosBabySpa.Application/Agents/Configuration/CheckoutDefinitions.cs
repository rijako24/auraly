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
    public CheckoutPaymentDefinition Payment { get; set; } = new();
    public Dictionary<string, OrderCheckoutPaymentMethodDefinition> PaymentMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TemplateWithPayment { get; set; }
    public string? TemplateNoPayment { get; set; }
    public string? ConfirmationOutcome { get; set; }
    public OrderCheckoutShippingDefinition Shipping { get; set; } = new();
    // Optional legacy/advanced overrides. Normal tenant seeds should rely on engine defaults by checkout mode.
    public Dictionary<string, string> RequiredFactRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SystemFactBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TemplateFactBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CheckoutPaymentDefinition
{
    public string Type { get; set; } = string.Empty;
    public int? Percentage { get; set; }
}

public sealed class OrderCheckoutPaymentMethodDefinition
{
    public string? Label { get; set; }
    public bool PaymentRequired { get; set; }
    public List<string> Aliases { get; set; } = [];
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
