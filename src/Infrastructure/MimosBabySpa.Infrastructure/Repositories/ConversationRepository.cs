using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _context;

    public ConversationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Conversation?> GetByUserNumberAsync(string userNumber)
    {
        return await _context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.UserNumber == userNumber);
    }

    public async Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber)
    {
        return await _context.Conversations
            .Include(c => c.Messages)
            .Where(c => c.BusinessId == businessId && c.UserNumber == userNumber)
            .OrderByDescending(c => c.OpenedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Conversation?> GetActiveByBusinessIdAndUserNumberAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        return await _context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(
                c => c.BusinessId == businessId
                    && c.UserNumber == userNumber
                    && c.Status == ConversationLifecycleStatus.Active,
                ct);
    }

    public async Task<bool> HasClosedConversationsAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        return await _context.Conversations.AnyAsync(
            c => c.BusinessId == businessId
                && c.UserNumber == userNumber
                && c.Status == ConversationLifecycleStatus.Closed,
            ct);
    }

    public Task<Conversation> CreateAsync(Conversation conversation)
    {
        _context.Conversations.Add(conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation> UpdateAsync(Conversation conversation)
    {
        _context.Conversations.Update(conversation);
        return Task.FromResult(conversation);
    }

    public async Task<Conversation?> GetByIdAsync(Guid conversationId)
    {
        return await _context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);
    }

    public async Task<(IReadOnlyList<Conversation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search,
        ConversationLifecycleStatus? status, CancellationToken ct,
        Guid? agentId = null)
    {
        var query = _context.Conversations
            .Where(c => c.BusinessId == businessId);

        if (agentId.HasValue)
            query = query.Where(c => c.AgentId == agentId.Value);

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                (c.UserNumber != null && c.UserNumber.ToLower().Contains(term)) ||
                (c.CustomerName != null && c.CustomerName.ToLower().Contains(term)) ||
                (c.LastMessage != null && c.LastMessage.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.LastActivityAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
