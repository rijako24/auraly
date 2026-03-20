using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class MessageService : IMessageService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IUnitOfWork unitOfWork, ILogger<MessageService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger     = logger;
    }

    public async Task<Domain.Entities.Message> SaveMessageAsync(
        Guid conversationId,
        string sender,
        string messageText)
    {
        try
        {
            var now = DateTime.UtcNow;
            var message = new Domain.Entities.Message
            {
                MessageId      = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender         = sender,
                MessageText    = messageText,
                Timestamp      = now
            };

            await _unitOfWork.Messages.CreateAsync(message);

            if (string.Equals(sender, "User", StringComparison.OrdinalIgnoreCase))
            {
                var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
                if (conversation != null)
                {
                    conversation.LastMessage = messageText;
                    conversation.Timestamp   = now;
                    await _unitOfWork.Conversations.UpdateAsync(conversation);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            _logger.LogDebug("Mensaje guardado: sender={Sender}, conv={ConversationId}", sender, conversationId);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar mensaje para conv={ConversationId}", conversationId);
            throw;
        }
    }

    public async Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId)
    {
        return await _unitOfWork.Messages.GetByConversationIdAsync(conversationId);
    }
}
