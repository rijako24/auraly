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
            .Include(a => a.PromptSections.Where(ps => ps.IsActive))
            .Include(a => a.KnowledgeSources.OrderBy(aks => aks.DisplayOrder))
                .ThenInclude(aks => aks.KnowledgeSource)
            .FirstOrDefaultAsync(a => a.AgentId == agentId && a.IsActive, ct);

    public async Task<Agent?> GetByIdIncludingInactiveAsync(Guid agentId, CancellationToken ct = default) =>
        await _db.Agents
            .Include(a => a.AgentType)
            .Include(a => a.PromptSections.OrderBy(ps => ps.DisplayOrder))
            .Include(a => a.KnowledgeSources.OrderBy(aks => aks.DisplayOrder))
                .ThenInclude(aks => aks.KnowledgeSource)
            .FirstOrDefaultAsync(a => a.AgentId == agentId, ct);

    public async Task<IReadOnlyList<Agent>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        await _db.Agents
            .Include(a => a.AgentType)
            .Where(a => a.BusinessId == businessId && a.IsActive)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Agent> Items, int TotalCount)> GetPagedByBusinessAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _db.Agents
            .Include(a => a.AgentType)
            .Where(a => a.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Name.ToLower().Contains(term) ||
                (a.Description != null && a.Description.ToLower().Contains(term)) ||
                (a.AgentType != null && a.AgentType.Name.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<Agent> AddAsync(Agent agent, CancellationToken ct = default)
    {
        if (agent.AgentId == Guid.Empty)
            agent.AgentId = Guid.NewGuid();
        agent.CreatedAt = DateTime.UtcNow;
        _db.Agents.Add(agent);
        return Task.FromResult(agent);
    }

    public Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        agent.UpdatedAt = DateTime.UtcNow;
        _db.Agents.Update(agent);
        return Task.CompletedTask;
    }

    public Task AddKnowledgeLinkAsync(AgentKnowledgeSource link, CancellationToken ct = default)
    {
        if (link.AgentKnowledgeSourceId == Guid.Empty)
            link.AgentKnowledgeSourceId = Guid.NewGuid();
        _db.AgentKnowledgeSources.Add(link);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsByBusinessAndNameAsync(
        Guid businessId, string name, Guid? exceptAgentId, CancellationToken ct = default)
    {
        var query = _db.Agents.Where(a => a.BusinessId == businessId &&
                                          a.Name.ToLower() == name.ToLower());
        if (exceptAgentId.HasValue)
            query = query.Where(a => a.AgentId != exceptAgentId.Value);
        return await query.AnyAsync(ct);
    }
}
