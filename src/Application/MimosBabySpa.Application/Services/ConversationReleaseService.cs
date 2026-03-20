using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Devuelve conversaciones escaladas al bot (Generic Flow: <see cref="FlowExecutionStateEntity.Owner"/>).
/// </summary>
public class ConversationReleaseService : IConversationReleaseService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowExecutionStateRepository _flowExecutionStateRepository;
    private readonly ILogger<ConversationReleaseService> _logger;

    public ConversationReleaseService(
        IConversationRepository conversationRepository,
        IAgentRepository agentRepository,
        IFlowExecutionStateRepository flowExecutionStateRepository,
        ILogger<ConversationReleaseService> logger)
    {
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _flowExecutionStateRepository = flowExecutionStateRepository;
        _logger = logger;
    }

    public async Task<ReleaseResult> ReleaseToBotAsync(Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Release: Conversación {ConvId} no encontrada", conversationId);
            return ReleaseResult.NotFound;
        }

        var agents = await _agentRepository.GetByBusinessAsync(conversation.BusinessId, ct);
        if (agents.Count == 0)
        {
            _logger.LogWarning("Release: sin agentes para BusinessId={BusinessId}", conversation.BusinessId);
            return ReleaseResult.NotFound;
        }

        var anyState = false;
        FlowExecutionStateEntity? humanOwned = null;

        var orderedAgentIds = new List<Guid>(agents.Count + 1);
        if (conversation.AgentId.HasValue)
            orderedAgentIds.Add(conversation.AgentId.Value);
        foreach (var ag in agents)
        {
            if (!conversation.AgentId.HasValue || ag.AgentId != conversation.AgentId.Value)
                orderedAgentIds.Add(ag.AgentId);
        }

        foreach (var agentId in orderedAgentIds)
        {
            var s = await _flowExecutionStateRepository.GetAsync(
                conversation.BusinessId, conversation.UserNumber, agentId, ct);
            if (s == null)
                continue;

            anyState = true;
            if (string.Equals(s.Owner, "Human", StringComparison.OrdinalIgnoreCase))
            {
                humanOwned = s;
                break;
            }
        }

        if (!anyState)
        {
            _logger.LogWarning("Release: sin FlowExecutionState para Conv={ConvId}", conversationId);
            return ReleaseResult.NotFound;
        }

        if (humanOwned == null)
        {
            _logger.LogDebug("Release: Conversación {ConvId} ya en Bot (flujo), idempotente", conversationId);
            return ReleaseResult.AlreadyWithBot;
        }

        humanOwned.Owner = "Bot";
        humanOwned.ConsecutiveDegradedTurns = 0;
        await _flowExecutionStateRepository.UpsertAsync(humanOwned, ct);

        _logger.LogInformation("Release: Conversación {ConvId} devuelta al bot (Generic Flow)", conversationId);
        return ReleaseResult.Released;
    }
}
