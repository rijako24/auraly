using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.StateManagement;
using Auraly.Platform.Domain.Models;

namespace Auraly.Platform.Application.Services;

/// <summary>
/// Devuelve conversaciones escaladas al bot. Una sola responsabilidad.
/// </summary>
public class ConversationReleaseService : IConversationReleaseService
{
    private readonly IConversationStateManager _stateManager;
    private readonly ILogger<ConversationReleaseService> _logger;

    public ConversationReleaseService(
        IConversationStateManager stateManager,
        ILogger<ConversationReleaseService> logger)
    {
        _stateManager = stateManager;
        _logger = logger;
    }

    public async Task<ReleaseResult> ReleaseToBotAsync(Guid conversationId, CancellationToken ct = default)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        if (state == null)
        {
            _logger.LogWarning("Release: Conversación {ConvId} no encontrada", conversationId);
            return ReleaseResult.NotFound;
        }

        if (state.Owner == ConversationOwner.Bot)
        {
            _logger.LogDebug("Release: Conversación {ConvId} ya en Bot, idempotente", conversationId);
            return ReleaseResult.AlreadyWithBot;
        }

        state.Owner = ConversationOwner.Bot;
        state.ConsecutiveDegradedTurns = 0;
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        _logger.LogInformation("Release: Conversación {ConvId} devuelta al bot", conversationId);
        return ReleaseResult.Released;
    }
}
