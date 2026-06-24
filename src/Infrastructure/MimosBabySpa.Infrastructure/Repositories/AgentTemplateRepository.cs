using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class AgentTemplateRepository : IAgentTemplateRepository
{
    private readonly ApplicationDbContext _context;

    public AgentTemplateRepository(ApplicationDbContext context) => _context = context;

    public Task<AgentTemplate?> GetByIdAsync(Guid agentTemplateId, CancellationToken ct = default) =>
        _context.AgentTemplates.FirstOrDefaultAsync(t => t.AgentTemplateId == agentTemplateId, ct);

    public Task<AgentTemplate?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        _context.AgentTemplates.FirstOrDefaultAsync(t => t.Key == key && t.IsActive, ct);

    public async Task<IReadOnlyList<AgentTemplate>> GetActiveAsync(CancellationToken ct = default) =>
        await _context.AgentTemplates.Where(t => t.IsActive).OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct);
}
