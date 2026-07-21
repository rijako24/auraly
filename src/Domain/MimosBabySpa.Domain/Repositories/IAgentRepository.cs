using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Admin: incluye agentes inactivos (sin filtrar IsActive).</summary>
    Task<Agent?> GetByIdForAdminAsync(Guid agentId, CancellationToken ct = default);

    Task<IReadOnlyList<Agent>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<Agent?> GetActiveCustomerByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<AgentType?> GetDefaultTypeAsync(CancellationToken ct = default);
    Task<bool> NameExistsAsync(Guid businessId, string name, CancellationToken ct = default);
    Task<Agent> AddAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);
}
