namespace MimosBabySpa.Application.Services;

public interface IConversationContextService
{
    Task AddContextAsync(Guid conversationId, string context);
    Task<int> AddContextBatchAsync(Guid conversationId, IEnumerable<string> contexts);
    Task<List<string>> GetAllContextAsync(Guid conversationId);
    Task ClearContextAsync(Guid conversationId);
    Task<string> BuildContextMessageAsync(Guid conversationId, Guid businessId);
}
