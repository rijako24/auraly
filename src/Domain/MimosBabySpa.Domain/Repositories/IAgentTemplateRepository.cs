using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAgentTemplateRepository
{
    Task<AgentTemplate?> GetByIdAsync(Guid agentTemplateId, CancellationToken ct = default);
    Task<AgentTemplate?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTemplate>> GetActiveAsync(CancellationToken ct = default);
}
