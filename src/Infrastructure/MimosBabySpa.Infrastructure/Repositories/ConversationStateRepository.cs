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
            existing.StateJson = entity.StateJson;
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
