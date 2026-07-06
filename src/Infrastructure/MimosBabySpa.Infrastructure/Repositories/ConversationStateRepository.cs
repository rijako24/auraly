using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ConversationStateRepository : IConversationStateRepository
{
    private readonly ApplicationDbContext _context;

    public ConversationStateRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ConversationStateEntity?> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await _context.ConversationStates
            .FirstOrDefaultAsync(s => s.ConversationId == conversationId, cancellationToken);
    }

    public async Task SaveAsync(ConversationStateEntity entity, CancellationToken cancellationToken = default)
    {
        var existing = await _context.ConversationStates
            .FirstOrDefaultAsync(s => s.ConversationId == entity.ConversationId, cancellationToken);

        if (existing != null)
        {
            existing.BusinessId = entity.BusinessId;
            existing.Owner = entity.Owner;
            existing.LastEscalatedAt = entity.LastEscalatedAt;
            existing.ConsecutiveDegradedTurns = entity.ConsecutiveDegradedTurns;
            existing.LastUserMessage = entity.LastUserMessage;
            existing.LastBotMessage = entity.LastBotMessage;
            existing.ActiveRequestStartedAtUtc = entity.ActiveRequestStartedAtUtc;
            existing.VerificationsJson = entity.VerificationsJson;
            existing.StageSnapshotsJson = entity.StageSnapshotsJson;
            existing.Version = entity.Version;
            existing.UpdatedAt = entity.UpdatedAt;
            _context.ConversationStates.Update(existing);
        }
        else
        {
            _context.ConversationStates.Add(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
