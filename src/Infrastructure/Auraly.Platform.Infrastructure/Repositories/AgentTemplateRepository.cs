using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

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
