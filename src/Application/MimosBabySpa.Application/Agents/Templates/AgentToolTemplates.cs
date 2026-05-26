namespace MimosBabySpa.Application.Agents.Templates;

/// <summary>
/// Plantillas por defecto de tools cuando el tenant no define override en AgentConfig.Templates.
/// </summary>
internal static class AgentToolTemplates
{
    public const string CheckoutWithDepositId = "checkout_with_deposit";
    public const string CheckoutNoDepositId = "checkout_no_deposit";
    public const string ReservationCreatedId = "reservation_created";
    public const string AvailabilitySlotsId = "availability_slots";

    public static string? Get(string templateId) =>
        templateId.ToLowerInvariant() switch
        {
            CheckoutWithDepositId => CheckoutWithDeposit,
            CheckoutNoDepositId => CheckoutNoDeposit,
            ReservationCreatedId => ReservationCreated,
            AvailabilitySlotsId => AvailabilitySlots,
            _ => null
        };

    public const string CheckoutWithDeposit =
        """
        📋 *Resumen de tu reserva*
        - Servicio: {{service_name}}
        - Fecha: {{date_formatted}}
        - Hora: {{time}}
        - Precio servicio: ${{service_price}}
        {{#each addons}}
        - {{name}}: ${{price}}
        {{/each}}
        - *TOTAL: ${{total}}*

        - Nombre del cliente: {{customer_name}}
        - Teléfono: {{customer_phone}}
        {{#if baby_age_months}}
        - Edad del bebé: {{baby_age_months}}
        {{/if}}
        {{#if baby_name}}
        - Nombre del bebé: {{baby_name}}
        {{/if}}

        💰 Para confirmar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.

        *Anticipo:* ${{deposit}} {{currency}}

        🔗 Paga en línea: {{link_url}}

        Una vez confirmado el anticipo, tu reserva quedará asegurada. ¡Estamos para ayudarte!
        """;

    public const string CheckoutNoDeposit =
        """
        📋 *Resumen de tu reserva*
        - Servicio: {{service_name}}
        - Fecha: {{date_formatted}}
        - Hora: {{time}}
        - Precio servicio: ${{service_price}}
        {{#each addons}}
        - {{name}}: ${{price}}
        {{/each}}
        - *TOTAL: ${{total}}*

        - Nombre del cliente: {{customer_name}}
        - Teléfono: {{customer_phone}}
        {{#if baby_age_months}}
        - Edad del bebé: {{baby_age_months}}
        {{/if}}
        {{#if baby_name}}
        - Nombre del bebé: {{baby_name}}
        {{/if}}

        ¿Confirmas la reserva con esta información?
        """;

    public const string ReservationCreated =
        """
        ✅ *¡Reserva confirmada!*

        Tu reserva ha sido registrada exitosamente para el {{date_formatted}} a las {{time}}.

        Te esperamos, {{customer_name}}. Si necesitas ayuda, escríbenos por aquí. 😊
        """;

    public const string AvailabilitySlots =
        """
        {{#if intro_message}}
        {{intro_message}}

        {{/if}}
        📅 *Horarios disponibles para {{date_formatted}}* ({{service_name}})

        {{#each slots}}
        - {{this}}
        {{/each}}

        ¿Cuál prefieres?
        """;
}
