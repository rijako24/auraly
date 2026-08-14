using Auraly.Platform.Application.Agents;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

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
        var active = await _agentRepository.GetActiveCustomerByBusinessAsync(businessId, ct);
        if (active is null)
            return null;

        return await _configProvider.GetConfigAsync(active.AgentId, ct);
    }
}
