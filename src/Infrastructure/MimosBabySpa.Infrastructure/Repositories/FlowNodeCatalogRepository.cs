using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class FlowNodeCatalogRepository : IFlowNodeCatalogRepository
{
    private readonly ApplicationDbContext _db;

    public FlowNodeCatalogRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FlowNodeCatalog>> GetActiveOrderedAsync(CancellationToken ct = default) =>
        await _db.FlowNodeCatalog
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);
}
