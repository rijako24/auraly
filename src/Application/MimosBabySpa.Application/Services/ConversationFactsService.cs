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
        var records = await GetAllRecordsAsync(conversationId, ct);
        return records.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversationId);
        return contexts
            .Select(c => new ConversationFactRecord(c.Field, c.Value, c.CreatedAt, c.UpdatedAt))
            .ToList();
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
        bool rememberAcrossRequests = false,
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

            if (rememberAcrossRequests)
            {
                await _customerMemory.RememberAsync(
                    businessId, conversation.UserNumber, key, value, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task ApplyBatchAsync(
        Guid conversationId,
        Guid businessId,
        IReadOnlyDictionary<string, string?> mutations,
        IReadOnlySet<string> rememberAcrossRequests,
        CancellationToken ct = default) =>
        _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (mutations.Count == 0)
                return;

            var cleared = mutations
                .Where(pair => pair.Value is null)
                .Select(pair => pair.Key)
                .ToList();
            if (cleared.Count > 0)
                await _unitOfWork.ConversationContexts.DeleteFieldsAsync(conversationId, cleared, ct);

            foreach (var (key, value) in mutations.Where(pair => pair.Value is not null))
                await _unitOfWork.ConversationContexts.CreateOrUpdateAsync(conversationId, key, value!);

            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation is not null)
            {
                if (mutations.TryGetValue(ConversationFactKeys.CustomerName, out var name) && name is not null)
                    conversation.CustomerName = name;
                if (mutations.TryGetValue(ConversationFactKeys.CustomerEmail, out var email) && email is not null)
                    conversation.CustomerEmail = email;

                if (mutations.ContainsKey(ConversationFactKeys.CustomerName)
                    || mutations.ContainsKey(ConversationFactKeys.CustomerEmail))
                {
                    conversation.Timestamp = DateTime.UtcNow;
                    await _unitOfWork.Conversations.UpdateAsync(conversation);
                }

                foreach (var key in rememberAcrossRequests)
                {
                    if (mutations.TryGetValue(key, out var value) && value is not null)
                        await _customerMemory.RememberAsync(businessId, conversation.UserNumber, key, value, ct);
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);
        }, ct);
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

    public async Task<IReadOnlyList<string>> ClearFieldsAsync(
        Guid conversationId,
        IReadOnlyCollection<string> fields,
        CancellationToken ct = default)
    {
        if (fields.Count == 0)
            return [];

        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversationId);
        var existingFields = contexts
            .Select(c => c.Field)
            .Where(field => fields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingFields.Count == 0)
            return [];

        await _unitOfWork.ConversationContexts.DeleteFieldsAsync(conversationId, existingFields, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return existingFields;
    }
}
