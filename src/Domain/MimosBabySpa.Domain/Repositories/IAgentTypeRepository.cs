using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IAgentTypeRepository
{
    Task<IReadOnlyList<AgentType>> GetActiveAsync(CancellationToken ct = default);
}
