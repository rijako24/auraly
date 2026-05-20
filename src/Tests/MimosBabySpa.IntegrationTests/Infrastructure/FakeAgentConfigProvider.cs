using MimosBabySpa.Application.Agents;

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
            Name = "Agente de Pruebas",
            SystemPrompt = "Eres un asistente de pruebas de MimosBabySpa. Ayuda a los clientes a reservar servicios.",
            Model = "gpt-4o",
            Temperature = 0.3f,
            MaxToolIterations = 8,
            ConsecutiveErrorEscalationThreshold = 3,
            EnabledToolNames =
            [
                "check_availability",
                "create_reservation",
                "get_service_catalog",
                "resolve_pricing",
                "generate_payment_link",
                "verify_payment",
                "escalate_to_human"
            ],
            EscalationContacts = []
        };
        return Task.FromResult(config);
    }
}
