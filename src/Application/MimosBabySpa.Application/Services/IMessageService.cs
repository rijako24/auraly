using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IMessageService
{
    Task<Domain.Entities.Message> SaveMessageAsync(Guid conversationId, string sender, string messageText, string intent);
    Task<string> ClassifyIntentAsync(string messageText, Domain.Entities.Conversation? conversation);
    Task<DTOs.IntentAndContextResult> ClassifyIntentAndExtractContextAsync(Guid businessId, string messageText, Domain.Entities.Conversation? conversation);
    Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId);
}
