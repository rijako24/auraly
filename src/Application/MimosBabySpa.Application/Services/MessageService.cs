using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class MessageService : IMessageService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIService _aiService;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IUnitOfWork unitOfWork, IAIService aiService, ILogger<MessageService> logger)
    {
        _unitOfWork = unitOfWork;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<Domain.Entities.Message> SaveMessageAsync(Guid conversationId, string sender, string messageText, string intent)
    {
        try
        {
            var message = new Domain.Entities.Message
            {
                MessageId = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender = sender,
                MessageText = messageText,
                Intent = intent,
                Timestamp = DateTime.UtcNow
            };

            var created = await _unitOfWork.Messages.CreateAsync(message);
            await _unitOfWork.SaveChangesAsync();
            
            _logger.LogDebug("Mensaje guardado: {Intent} de {Sender}", intent, sender);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar mensaje");
            throw;
        }
    }

    public async Task<string> ClassifyIntentAsync(string messageText, Domain.Entities.Conversation? conversation)
    {
        return await _aiService.ClassifyIntentAsync(messageText, conversation);
    }

    public async Task<DTOs.IntentAndContextResult> ClassifyIntentAndExtractContextAsync(Guid businessId, string messageText, Domain.Entities.Conversation? conversation)
    {
        return await _aiService.ClassifyIntentAndExtractContextAsync(businessId, messageText, conversation);
    }

    public async Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId)
    {
        return await _unitOfWork.Messages.GetByConversationIdAsync(conversationId);
    }
}
