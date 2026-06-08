using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class ConversationFactsService : IConversationFactsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerMemoryService _customerMemory;

    public ConversationFactsService(IUnitOfWork unitOfWork, ICustomerMemoryService customerMemory)
    {
        _unitOfWork = unitOfWork;
        _customerMemory = customerMemory;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default)
    {
        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversationId);
        return contexts.ToDictionary(c => c.Field, c => c.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default)
    {
        var ctx = await _unitOfWork.ConversationContexts.GetByConversationIdAndFieldAsync(conversationId, key);
        return string.IsNullOrWhiteSpace(ctx?.Value) ? null : ctx.Value.Trim();
    }

    public async Task SetAsync(
        Guid conversationId,
        Guid businessId,
        string key,
        string value,
        bool persistsAcrossConversations = false,
        CancellationToken ct = default)
    {
        await _unitOfWork.ConversationContexts.CreateOrUpdateAsync(conversationId, key, value);

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);

        if (conversation is not null)
        {
            if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase))
                conversation.CustomerName = value;
            else if (key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase))
                conversation.CustomerEmail = value;

            if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase)
                || key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase))
            {
                conversation.Timestamp = DateTime.UtcNow;
                await _unitOfWork.Conversations.UpdateAsync(conversation);
            }

            if (persistsAcrossConversations)
            {
                await _customerMemory.RememberAsync(
                    businessId, conversation.UserNumber, key, value, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ClearNonPersistentAsync(
        Guid conversationId,
        IReadOnlyCollection<string> persistentKeys,
        CancellationToken ct = default)
    {
        var keep = persistentKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversationId);
        var fieldsToDelete = contexts
            .Select(c => c.Field)
            .Where(field => !keep.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fieldsToDelete.Count == 0)
            return [];

        await _unitOfWork.ConversationContexts.DeleteFieldsAsync(conversationId, fieldsToDelete, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return fieldsToDelete;
    }
}
