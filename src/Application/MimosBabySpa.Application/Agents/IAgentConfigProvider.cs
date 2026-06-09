namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Provee la configuración de un agente por ID.
/// La implementación puede cachear el resultado.
/// </summary>
public interface IAgentConfigProvider
{
    Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default);
}
