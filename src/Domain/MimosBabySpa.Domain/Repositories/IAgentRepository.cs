using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAgentRepository
{
    /// <summary>Active agent only (runtime / WhatsApp).</summary>
    Task<Agent?> GetByIdAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Admin: load agent regardless of <see cref="Agent.IsActive"/>.</summary>
    Task<Agent?> GetByIdIncludingInactiveAsync(Guid agentId, CancellationToken ct = default);

    Task<IReadOnlyList<Agent>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default);

    Task<(IReadOnlyList<Agent> Items, int TotalCount)> GetPagedByBusinessAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct = default);

    Task<Agent> AddAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);

    Task AddKnowledgeLinkAsync(AgentKnowledgeSource link, CancellationToken ct = default);

    Task<bool> ExistsByBusinessAndNameAsync(
        Guid businessId, string name, Guid? exceptAgentId, CancellationToken ct = default);
}
