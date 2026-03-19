using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class FlowExecutionStateRepository : IFlowExecutionStateRepository
{
    private readonly ApplicationDbContext _db;

    public FlowExecutionStateRepository(ApplicationDbContext db) => _db = db;

    public async Task<FlowExecutionStateEntity?> GetAsync(
        Guid businessId, string userIdentifier, Guid agentId, CancellationToken ct = default) =>
        await _db.FlowExecutionStates
            .FirstOrDefaultAsync(s =>
                s.BusinessId == businessId &&
                s.UserIdentifier == userIdentifier &&
                s.AgentId == agentId, ct);

    public async Task<FlowExecutionStateEntity> UpsertAsync(
        FlowExecutionStateEntity state, CancellationToken ct = default)
    {
        var existing = await _db.FlowExecutionStates
            .FirstOrDefaultAsync(s =>
                s.BusinessId == state.BusinessId &&
                s.UserIdentifier == state.UserIdentifier &&
                s.AgentId == state.AgentId, ct);

        if (existing == null)
        {
            state.FlowExecutionStateId = Guid.NewGuid();
            state.CreatedAt = DateTime.UtcNow;
            _db.FlowExecutionStates.Add(state);
        }
        else
        {
            existing.CurrentNodeId = state.CurrentNodeId;
            existing.IsWaitingForUser = state.IsWaitingForUser;
            existing.FlowDefinitionId = state.FlowDefinitionId;
            existing.Owner = state.Owner;
            existing.Version = state.Version;
            existing.UpdatedAt = state.UpdatedAt;
            existing.VariablesJson = state.VariablesJson;
            existing.FlagsJson = state.FlagsJson;
            existing.ActionResultsJson = state.ActionResultsJson;
            existing.TraceJson = state.TraceJson;
            existing.PreviousSessionJson = state.PreviousSessionJson;
        }

        await _db.SaveChangesAsync(ct);
        return existing ?? state;
    }

    public async Task DeleteAsync(
        Guid businessId, string userIdentifier, Guid agentId, CancellationToken ct = default)
    {
        var entity = await _db.FlowExecutionStates
            .FirstOrDefaultAsync(s =>
                s.BusinessId == businessId &&
                s.UserIdentifier == userIdentifier &&
                s.AgentId == agentId, ct);

        if (entity != null)
        {
            _db.FlowExecutionStates.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}
