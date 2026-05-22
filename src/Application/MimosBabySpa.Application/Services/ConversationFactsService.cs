using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class ConversationFactsService : IConversationFactsService
{
    private readonly IUnitOfWork _unitOfWork;

    public ConversationFactsService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

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

    public async Task SetAsync(Guid conversationId, Guid businessId, string key, string value, CancellationToken ct = default)
    {
        await _unitOfWork.ConversationContexts.CreateOrUpdateAsync(conversationId, key, value);

        if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase)
            || key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase))
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation is not null)
            {
                if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase))
                    conversation.CustomerName = value;
                else
                    conversation.CustomerEmail = value;

                conversation.Timestamp = DateTime.UtcNow;
                await _unitOfWork.Conversations.UpdateAsync(conversation);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }
}
