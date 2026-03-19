using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IFlowExecutionStateRepository
{
    Task<FlowExecutionStateEntity?> GetAsync(Guid businessId, string userIdentifier, Guid agentId, CancellationToken ct = default);
    Task<FlowExecutionStateEntity> UpsertAsync(FlowExecutionStateEntity state, CancellationToken ct = default);
    Task DeleteAsync(Guid businessId, string userIdentifier, Guid agentId, CancellationToken ct = default);
}
