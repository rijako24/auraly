using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Agente conversacional autónomo que usa OpenAI Function Calling para decidir cuándo usar herramientas
/// </summary>
public interface IConversationAgent
{
    Task<string> ProcessMessageAsync(
        Guid businessId,
        string userMessage,
        Conversation conversation,
        Lead? lead,
        CancellationToken cancellationToken = default);
}
