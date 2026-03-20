using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IFlowNodeCatalogRepository
{
    Task<IReadOnlyList<FlowNodeCatalog>> GetActiveOrderedAsync(CancellationToken ct = default);
}
