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
            PromptSections =
            [
                new PromptSection
                {
                    Id = "persona",
                    Order = 10,
                    Content = """
                        Eres Mimo, asistente de MimosBabySpa.

                        ## SALUDO Y PRESENTACION
                        En el primer mensaje saluda y preséntate. En turnos siguientes no repitas el saludo.
                        """
                }
            ],
            CapabilityPacks = ["booking"],
            HumanMessages = new AgentHumanMessages
            {
                EscalationUserMessage = "Te conecto con un agente humano en un momento.",
                SemanticTriggerLineFormat = "- `{0}`: Úsalo cuando {1}."
            },
            Model = "gpt-4.1-mini",
            Temperature = 0.3f,
            MaxToolIterations = 8,
            ConsecutiveErrorEscalationThreshold = 3,
            EnabledToolNames =
            [
                "check_availability",
                "prepare_checkout",
                "create_reservation",
                "get_service_catalog",
                "set_fact",
                "verify_payment",
                "escalate_to_human"
            ],
            EscalationContacts = []
        };
        return Task.FromResult(config);
    }
}
