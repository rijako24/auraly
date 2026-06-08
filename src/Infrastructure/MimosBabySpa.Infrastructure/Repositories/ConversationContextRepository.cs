using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ConversationContextRepository : IConversationContextRepository
{
    private readonly ApplicationDbContext _context;

    public ConversationContextRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ConversationContext?> GetByConversationIdAndFieldAsync(Guid conversationId, string field)
    {
        return await _context.ConversationContexts
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId && c.Field == field);
    }

    public async Task<ConversationContext> CreateOrUpdateAsync(Guid conversationId, string field, string value)
    {
        var existing = await GetByConversationIdAndFieldAsync(conversationId, field);
        
        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            _context.ConversationContexts.Update(existing);
            return existing;
        }
        else
        {
            var newContext = new ConversationContext
            {
                ConversationContextId = Guid.NewGuid(),
                ConversationId = conversationId,
                Field = field,
                Value = value,
                CreatedAt = DateTime.UtcNow
            };
            _context.ConversationContexts.Add(newContext);
            return newContext;
        }
    }

    public async Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId)
    {
        return await _context.ConversationContexts
            .Where(c => c.ConversationId == conversationId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task DeleteFieldsAsync(Guid conversationId, IReadOnlyCollection<string> fields, CancellationToken ct = default)
    {
        if (fields.Count == 0)
            return;

        var contexts = await _context.ConversationContexts
            .Where(c => c.ConversationId == conversationId && fields.Contains(c.Field))
            .ToListAsync(ct);

        _context.ConversationContexts.RemoveRange(contexts);
    }

    public async Task DeleteByConversationIdAsync(Guid conversationId)
    {
        var contexts = await _context.ConversationContexts
            .Where(c => c.ConversationId == conversationId)
            .ToListAsync();
        
        _context.ConversationContexts.RemoveRange(contexts);
    }
}
