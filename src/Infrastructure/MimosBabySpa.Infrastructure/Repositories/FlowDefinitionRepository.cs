using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class FlowDefinitionRepository : IFlowDefinitionRepository
{
    private readonly ApplicationDbContext _db;

    public FlowDefinitionRepository(ApplicationDbContext db) => _db = db;

    public async Task<FlowDefinitionEntity?> GetActiveByAgentAsync(
        Guid agentId, CancellationToken ct = default) =>
        await _db.FlowDefinitions
            .Where(f => f.AgentId == agentId && f.IsActive)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<FlowDefinitionEntity?> GetByIdAsync(
        Guid flowDefinitionId, CancellationToken ct = default) =>
        await _db.FlowDefinitions.FindAsync([flowDefinitionId], ct);

    public Task<FlowDefinitionEntity> AddAsync(
        FlowDefinitionEntity definition, CancellationToken ct = default)
    {
        if (definition.FlowDefinitionId == Guid.Empty)
            definition.FlowDefinitionId = Guid.NewGuid();
        definition.CreatedAt = DateTime.UtcNow;
        _db.FlowDefinitions.Add(definition);
        return Task.FromResult(definition);
    }

    public Task UpdateAsync(FlowDefinitionEntity definition, CancellationToken ct = default)
    {
        definition.UpdatedAt = DateTime.UtcNow;
        _db.FlowDefinitions.Update(definition);
        return Task.CompletedTask;
    }
}
