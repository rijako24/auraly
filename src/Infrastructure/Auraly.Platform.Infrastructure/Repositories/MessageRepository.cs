using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _context;

    public MessageRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Message> CreateAsync(Message message)
    {
        _context.Messages.Add(message);
        return Task.FromResult(message);
    }

    public async Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId)
    {
        return await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Message>> GetRecentByConversationIdAsync(
        Guid conversationId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        var recent = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        recent.Reverse();
        return recent;
    }

    public async Task<Message?> GetByIdAsync(Guid messageId)
    {
        return await _context.Messages.FindAsync(messageId);
    }

    public async Task<(IReadOnlyList<Message> Items, int TotalCount)> GetPagedByConversationIdAsync(
        Guid conversationId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
