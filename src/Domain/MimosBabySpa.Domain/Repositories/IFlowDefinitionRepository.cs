using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IFlowDefinitionRepository
{
    Task<FlowDefinitionEntity?> GetActiveByAgentAsync(Guid agentId, CancellationToken ct = default);
    Task<FlowDefinitionEntity?> GetByIdAsync(Guid flowDefinitionId, CancellationToken ct = default);
    Task<FlowDefinitionEntity> AddAsync(FlowDefinitionEntity definition, CancellationToken ct = default);
    Task UpdateAsync(FlowDefinitionEntity definition, CancellationToken ct = default);
}
