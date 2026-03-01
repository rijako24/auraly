namespace MimosBabySpa.Application.Services;

/// <summary>
/// Devuelve conversaciones escaladas al bot. Idempotente.
/// </summary>
public interface IConversationReleaseService
{
    /// <summary>
    /// Devuelve la conversación al bot. Si ya está en Bot, no-op.
    /// Retorna el resultado del release.
    /// </summary>
    Task<ReleaseResult> ReleaseToBotAsync(Guid conversationId, CancellationToken ct = default);
}

public enum ReleaseResult
{
    Released,
    AlreadyWithBot,
    NotFound
}
