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
            var message = new Domain.Entities.Message
            {
                MessageId      = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender         = sender,
                MessageText    = messageText,
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

    public async Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId)
    {
        return await _unitOfWork.Messages.GetByConversationIdAsync(conversationId);
    }

    public async Task<IReadOnlyList<Domain.Entities.Message>> GetRecentConversationHistoryAsync(
        Guid conversationId, int limit, CancellationToken ct = default)
    {
        return await _unitOfWork.Messages.GetRecentByConversationIdAsync(conversationId, limit, ct);
    }
}
