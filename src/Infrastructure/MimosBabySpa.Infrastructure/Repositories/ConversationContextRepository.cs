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

    public async Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId)
    {
        return await _context.ConversationContexts
            .Where(cc => cc.ConversationId == conversationId)
            .OrderBy(cc => cc.CreatedAt)
            .ToListAsync();
    }

    public async Task<ConversationContext> CreateAsync(Guid conversationId, string context)
    {
        var newContext = new ConversationContext
        {
            ConversationContextId = Guid.NewGuid(),
            ConversationId = conversationId,
            Context = context,
            CreatedAt = DateTime.UtcNow
        };

        _context.ConversationContexts.Add(newContext);
        return newContext;
    }

    public async Task DeleteAsync(Guid conversationContextId)
    {
        var context = await _context.ConversationContexts.FindAsync(conversationContextId);
        if (context != null)
        {
            _context.ConversationContexts.Remove(context);
        }
    }

    public async Task DeleteByConversationIdAsync(Guid conversationId)
    {
        var contexts = await GetByConversationIdAsync(conversationId);
        _context.ConversationContexts.RemoveRange(contexts);
    }

    public async Task<bool> ExistsAsync(Guid conversationId, string context)
    {
        var normalizedContext = context.Trim();
        return await _context.ConversationContexts
            .AnyAsync(cc => 
                cc.ConversationId == conversationId && 
                cc.Context.Trim().ToLower() == normalizedContext.ToLower());
    }

    public async Task<int> CreateBatchAsync(Guid conversationId, IEnumerable<string> contexts)
    {
        // Normalizar y filtrar contextos vacíos
        var normalizedContexts = contexts
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalizedContexts.Any())
        {
            return 0;
        }

        var contextsToAdd = new List<string>();
        
        foreach (var context in normalizedContexts)
        {
            var contextLower = context.ToLower();
            var exists = await _context.ConversationContexts
                .AnyAsync(cc => cc.ConversationId == conversationId && 
                               cc.Context.Trim().ToLower() == contextLower);
            
            if (!exists)
            {
                contextsToAdd.Add(context);
            }
        }

        if (!contextsToAdd.Any())
        {
            return 0;
        }

        // Crear entidades para insertar
        var newContexts = contextsToAdd.Select(context => new ConversationContext
        {
            ConversationContextId = Guid.NewGuid(),
            ConversationId = conversationId,
            Context = context,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        // Insertar en batch
        await _context.ConversationContexts.AddRangeAsync(newContexts);
        
        return newContexts.Count;
    }
}
