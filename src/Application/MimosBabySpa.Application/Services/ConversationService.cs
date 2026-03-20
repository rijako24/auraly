using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ConversationService : IConversationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(IUnitOfWork unitOfWork, ILogger<ConversationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(
        Guid businessId,
        string userNumber,
        string? customerName = null,
        Guid? agentId = null)
    {
        try
        {
            var existingConversation = await _unitOfWork.Conversations.GetByBusinessIdAndUserNumberAsync(businessId, userNumber);
            
            if (existingConversation != null)
            {
                var needsSave = false;
                if (!string.IsNullOrEmpty(customerName) && string.IsNullOrEmpty(existingConversation.CustomerName))
                {
                    existingConversation.CustomerName = customerName;
                    needsSave = true;
                }

                if (agentId.HasValue && existingConversation.AgentId != agentId)
                {
                    existingConversation.AgentId = agentId;
                    needsSave = true;
                }

                if (needsSave)
                {
                    await _unitOfWork.Conversations.UpdateAsync(existingConversation);
                    await _unitOfWork.SaveChangesAsync();
                }

                return existingConversation;
            }

            var newConversation = new Domain.Entities.Conversation
            {
                ConversationId = Guid.NewGuid(),
                BusinessId = businessId,
                AgentId = agentId,
                UserNumber = userNumber,
                CustomerName = customerName,
                Timestamp = DateTime.UtcNow
            };

            var created = await _unitOfWork.Conversations.CreateAsync(newConversation);
            await _unitOfWork.SaveChangesAsync();
            
            _logger.LogDebug("Nueva conversación creada para {UserNumber} en negocio {BusinessId}", userNumber, businessId);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener o crear conversación para {UserNumber} en negocio {BusinessId}", userNumber, businessId);
            throw;
        }
    }

    public async Task<Domain.Entities.Conversation?> GetConversationByIdAsync(Guid conversationId)
    {
        return await _unitOfWork.Conversations.GetByIdAsync(conversationId);
    }
}
