namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Provee la configuración de un agente por ID.
/// La implementación puede cachear el resultado.
/// </summary>
public interface IAgentConfigProvider
{
    Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentConfig> GetConfigForAdminAsync(Guid agentId, CancellationToken ct = default);
    void Invalidate(Guid agentId);
}
