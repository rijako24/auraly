using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ConversationService : IConversationService
{
    private readonly IConversationLifecycleService _lifecycle;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationLifecycleService lifecycle,
        IUnitOfWork unitOfWork,
        ILogger<ConversationService> logger)
    {
        _lifecycle = lifecycle;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(
        Guid businessId, string userNumber, string? customerName = null) =>
        _lifecycle.GetOrOpenForCustomerAsync(businessId, userNumber, customerName);

    public Task UpdateConversationContextAsync(Guid conversationId, string? lastMessage) =>
        _lifecycle.TouchActivityAsync(conversationId, lastMessage);

    public async Task UpdateConversationAsync(Domain.Entities.Conversation conversation, CancellationToken ct = default)
    {
        await _unitOfWork.Conversations.UpdateAsync(conversation);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task<Domain.Entities.Conversation?> GetConversationByIdAsync(Guid conversationId) =>
        _unitOfWork.Conversations.GetByIdAsync(conversationId);

    public Task<bool> HasClosedConversationsAsync(Guid businessId, string userNumber, CancellationToken ct = default) =>
        _unitOfWork.Conversations.HasClosedConversationsAsync(businessId, userNumber, ct);
}
