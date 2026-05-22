namespace MimosBabySpa.Application.Agents.Templates;

/// <summary>
/// Plantillas mínimas si el prompt del agente no declara el bloque [template: ...].
/// </summary>
internal static class TemplateFallbackCatalog
{
    public static string? Get(string templateId) =>
        templateId.ToLowerInvariant() switch
        {
            "checkout_with_deposit" =>
                """
                📋 *Resumen de tu reserva*
                - Servicio: {{service_name}}
                - Fecha: {{date_formatted}}
                - Hora: {{time}}
                - *TOTAL: ${{total}}*
                - Anticipo ({{deposit_pct}}%): ${{deposit}} {{currency}}
                🔗 Paga en línea: {{link_url}}
                """,
            "checkout_no_deposit" =>
                """
                📋 *Resumen de tu reserva*
                - Servicio: {{service_name}}
                - Fecha: {{date_formatted}}
                - Hora: {{time}}
                - *TOTAL: ${{total}}*
                ¿Confirmas la reserva con esta información?
                """,
            "reservation_created" =>
                """
                ✅ *¡Reserva confirmada!*
                Te esperamos el {{date_formatted}} a las {{time}}, {{customer_name}}.
                """,
            "availability_slots" =>
                """
                📅 *Horarios disponibles para {{date_formatted}}* ({{service_name}})

                {{#each slots}}
                - {{this}}
                {{/each}}

                ¿Cuál prefieres?
                """,
            _ => null
        };
}
