namespace MimosBabySpa.Application.Agents.Packs.Booking;

/// <summary>
/// Plantillas neutras del booking-pack (sin copy de tenant).
/// Los tenants pueden override en AgentConfig.Templates.
/// </summary>
internal static class BookingDefaultTemplates
{
    public const string CheckoutWithDepositId = "checkout_with_deposit";
    public const string CheckoutNoDepositId = "checkout_no_deposit";
    public const string ReservationCreatedId = "reservation_created";
    public const string AvailabilitySlotsId = "availability_slots";

    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CheckoutWithDepositId] = CheckoutWithDeposit,
            [CheckoutNoDepositId] = CheckoutNoDeposit,
            [ReservationCreatedId] = ReservationCreated,
            [AvailabilitySlotsId] = AvailabilitySlots
        };

    public const string CheckoutWithDeposit =
        """
        📋 *Booking summary*
        - Service: {{service_name}}
        - Date: {{date_formatted}}
        - Time: {{time}}
        - Service price: ${{service_price}}
        {{#each addons}}
        - {{name}}: ${{price}}
        {{/each}}
        - *TOTAL: ${{total}}*

        - Customer: {{customer_name}}
        - Phone: {{customer_phone}}

        💰 A deposit of {{deposit_pct}}% is required to confirm.

        *Deposit:* ${{deposit}} {{currency}}

        🔗 Pay online: {{link_url}}
        """;

    public const string CheckoutNoDeposit =
        """
        📋 *Booking summary*
        - Service: {{service_name}}
        - Date: {{date_formatted}}
        - Time: {{time}}
        - Service price: ${{service_price}}
        {{#each addons}}
        - {{name}}: ${{price}}
        {{/each}}
        - *TOTAL: ${{total}}*

        - Customer: {{customer_name}}
        - Phone: {{customer_phone}}

        Confirm this booking?
        """;

    public const string ReservationCreated =
        """
        ✅ *Booking confirmed*

        Your booking is registered for {{date_formatted}} at {{time}}.

        See you soon, {{customer_name}}.
        """;

    public const string AvailabilitySlots =
        """
        📅 *Available times for {{date_formatted}}* ({{service_name}})

        {{#each slots}}
        - {{this}}
        {{/each}}

        Which time do you prefer?
        """;
}
