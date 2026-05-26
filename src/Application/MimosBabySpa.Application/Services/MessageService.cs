using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Messaging;
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
            var text = FitWhatsAppTextBody(conversationId, messageText);
            var message = new Domain.Entities.Message
            {
                MessageId      = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender         = sender,
                MessageText    = text,
                Timestamp      = DateTime.UtcNow
            };

            var created = await _unitOfWork.Messages.CreateAsync(message);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogDebug("Mensaje guardado: sender={Sender}, conv={ConversationId}", sender, conversationId);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar mensaje para conv={ConversationId}", conversationId);
            throw;
        }
    }

    private string FitWhatsAppTextBody(Guid conversationId, string messageText)
    {
        if (messageText.Length <= WhatsAppMessageLimits.MaxTextBodyChars)
            return messageText;

        _logger.LogWarning(
            "Mensaje truncado de {Original} a {Max} caracteres (límite WhatsApp) para conv={ConversationId}",
            messageText.Length,
            WhatsAppMessageLimits.MaxTextBodyChars,
            conversationId);

        return messageText[..WhatsAppMessageLimits.MaxTextBodyChars].Trim();
    }

    public async Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId)
    {
        return await _unitOfWork.Messages.GetByConversationIdAsync(conversationId);
    }
}
