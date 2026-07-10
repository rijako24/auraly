using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// IAgentConfigProvider falso que devuelve un AgentConfig de prueba sin acceso a BD.
/// </summary>
public class FakeAgentConfigProvider : IAgentConfigProvider
{
    private readonly Guid _businessId;

    public FakeAgentConfigProvider(Guid businessId)
    {
        _businessId = businessId;
    }

    public Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = _businessId,
            Name = "Mimo",
            Persona = """
                Eres Mimo, asistente de MimosBabySpa.

                ## SALUDO Y PRESENTACION
                En el primer mensaje saluda y presentate. En turnos siguientes no repitas el saludo.
                """,
            Model = "gpt-4.1-mini",
            Temperature = 0.3f,
            MaxToolIterations = 8,
            ConsecutiveErrorEscalationThreshold = 3,
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Role = "booking.service", Source = "user", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "reservation_date", Role = "booking.date", Source = "user", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "reservation_time", Role = "booking.time", Source = "user", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "customer_name", Role = "customer.name", Source = "user", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "customer_phone", Role = "customer.phone", Source = "user", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "add_ons", Role = "booking.addons", Source = "user", Scope = FactScopes.Request }
            ],
            Checkout = new CheckoutDefinitions
            {
                Currency = "COP",
                Modes =
                {
                    ["reservation"] = new CheckoutModeDefinition
                    {
                        PaymentMethods =
                        {
                            ["wompi"] = new CheckoutPaymentMethodDefinition
                            {
                                Label = "Wompi",
                                Template = "checkout_with_deposit",
                                ConfirmationOutcome = "reservation",
                                Payment = new CheckoutPaymentDefinition { Percentage = 100 }
                            }
                        }
                    }
                }
            },
            Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["availability_slots"] = "Espacios disponibles para {{date_formatted}}: {{#each options}}{{this}} {{/each}}",
                ["checkout_with_deposit"] = "Resumen: {{service_name}} {{date_formatted}} {{time}} total {{total}} {{currency}} {{link_url}}"
            },            EnabledToolNames =
            [
                "check_availability",
                "prepare_checkout",
                "create_reservation",
                "get_service_catalog",
                "resolve_service_selection",
                "get_compatible_add_ons",
                "get_service_fulfillment",
                "set_fact",
                "verify_payment",
                "escalate_to_human"
            ],
            Escalations = new MimosBabySpa.Application.Agents.Configuration.EscalationDefinitions()
        };
        return Task.FromResult(config);
    }
}
