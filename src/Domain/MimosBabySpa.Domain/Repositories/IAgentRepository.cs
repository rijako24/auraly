using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid agentId, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<Agent> AddAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);
}
