using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class AgentRepository : IAgentRepository
{
    private readonly ApplicationDbContext _db;

    public AgentRepository(ApplicationDbContext db) => _db = db;

    public async Task<Agent?> GetByIdAsync(Guid agentId, CancellationToken ct = default) =>
        await _db.Agents
            .Include(a => a.AgentType)
            .FirstOrDefaultAsync(a => a.AgentId == agentId && a.IsActive, ct);

    public async Task<IReadOnlyList<Agent>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        await _db.Agents
            .Include(a => a.AgentType)
            .Where(a => a.BusinessId == businessId && a.IsActive)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

    public async Task<Agent> AddAsync(Agent agent, CancellationToken ct = default)
    {
        agent.AgentId = Guid.NewGuid();
        agent.CreatedAt = DateTime.UtcNow;
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);
        return agent;
    }

    public async Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        agent.UpdatedAt = DateTime.UtcNow;
        _db.Agents.Update(agent);
        await _db.SaveChangesAsync(ct);
    }
}
