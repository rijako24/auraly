using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class AgentTypeRepository : IAgentTypeRepository
{
    private readonly ApplicationDbContext _db;

    public AgentTypeRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AgentType>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.AgentTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
}
