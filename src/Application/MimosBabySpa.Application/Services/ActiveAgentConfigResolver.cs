using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve la configuración del agente activo de un negocio (SettingsJson parseado).
/// </summary>
public interface IActiveAgentConfigResolver
{
    Task<AgentConfig?> GetActiveConfigAsync(Guid businessId, CancellationToken ct = default);
}

public sealed class ActiveAgentConfigResolver : IActiveAgentConfigResolver
{
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentConfigProvider _configProvider;

    public ActiveAgentConfigResolver(
        IAgentRepository agentRepository,
        IAgentConfigProvider configProvider)
    {
        _agentRepository = agentRepository;
        _configProvider = configProvider;
    }

    public async Task<AgentConfig?> GetActiveConfigAsync(Guid businessId, CancellationToken ct = default)
    {
        var agents = await _agentRepository.GetByBusinessAsync(businessId, ct);
        var active = agents.FirstOrDefault();
        if (active is null)
            return null;

        return await _configProvider.GetConfigAsync(active.AgentId, ct);
    }
}
