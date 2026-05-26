using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Application.Agents.Packs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Único punto de entrada para procesar un turno de conversación.
/// Valida precondiciones (owner=human, kill-switch) y delega al FlowEngine.
/// </summary>
public sealed class AgentConversationService : IAgentConversationService
{
    private readonly IAgentConfigProvider _configProvider;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IConversationFactsService _factsService;
    private readonly IConversationService _conversationService;
    private readonly IBusinessClock _businessClock;
    private readonly IFactHydrator _factHydrator;
    private readonly IFlowEngine _flowEngine;
    private readonly IReadOnlyList<IPackContextLoader> _packContextLoaders;
    private readonly ILogger<AgentConversationService> _logger;

    public AgentConversationService(
        IAgentConfigProvider configProvider,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IConversationFactsService factsService,
        IConversationService conversationService,
        IBusinessClock businessClock,
        IFactHydrator factHydrator,
        IFlowEngine flowEngine,
        IEnumerable<IPackContextLoader> packContextLoaders,
        ILogger<AgentConversationService> logger)
    {
        _configProvider = configProvider;
        _stateManager = stateManager;
        _messageService = messageService;
        _factsService = factsService;
        _conversationService = conversationService;
        _businessClock = businessClock;
        _factHydrator = factHydrator;
        _flowEngine = flowEngine;
        _packContextLoaders = packContextLoaders.ToList();
        _logger = logger;
    }

    public async Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        string? channelPhone = null,
        CancellationToken cancellationToken = default)
    {
        var config = await _configProvider.GetConfigAsync(agentId, cancellationToken);
        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, config.BusinessId, channelPhone ?? string.Empty, cancellationToken);

        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogInformation("Conv {ConvId}: Owner=Human, skipping bot", conversationId);
            return AgentTurnResult.Ok(string.Empty);
        }

        if (IsKillSwitchPhrase(userMessage, config.KillSwitchPhrases))
        {
            _logger.LogInformation("Conv {ConvId}: kill-switch triggered", conversationId);
            return await HandleKillSwitchAsync(config, state, conversationId, userMessage, cancellationToken);
        }

        userMessage = SanitizeInput(userMessage, config.OperationalLimits.InputMaxChars);

        var session = await LoadTurnSessionAsync(
            config, state, conversationId, channelPhone, cancellationToken);

        // Engagement context (for LLM hint: new vs returning customer)
        var history = await _messageService.GetConversationHistoryAsync(conversationId);
        session.Facts["session.engagement"] = ResolveEngagementKey(session.Conversation.UserNumber, history);

        return await _flowEngine.ProcessTurnAsync(config, session, userMessage, cancellationToken);
    }

    private async Task<AgentTurnResult> HandleKillSwitchAsync(
        AgentConfig config,
        ConversationState state,
        Guid conversationId,
        string userMessage,
        CancellationToken ct)
    {
        state.Owner = ConversationOwner.Human;
        state.LastEscalatedAt = DateTime.UtcNow;

        await _messageService.SaveMessageAsync(conversationId, "user", userMessage);

        var escalateMsg = string.IsNullOrWhiteSpace(config.HumanMessages.EscalationUserMessage)
            ? "Te voy a conectar con una persona de nuestro equipo. En un momento te atienden."
            : config.HumanMessages.EscalationUserMessage.Trim();

        await _messageService.SaveMessageAsync(conversationId, "bot", escalateMsg);
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        return AgentTurnResult.Ok(escalateMsg, escalated: true);
    }

    private async Task<AgentToolContext> LoadTurnSessionAsync(
        AgentConfig config,
        ConversationState state,
        Guid conversationId,
        string? channelPhone,
        CancellationToken ct)
    {
        var facts = await _factsService.GetAllAsync(conversationId, ct);
        _logger.LogInformation("Conv {C}: loaded stored facts [{Facts}]", conversationId,
            string.Join(',', facts.Keys));

        var conversation = await _conversationService.GetConversationByIdAsync(conversationId)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, ct);
        var resolvedPhone = channelPhone?.Trim() ?? conversation.UserNumber;

        var mutableFacts = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);

        _factHydrator.Hydrate(config.FactSchema, mutableFacts, new FactHydratorContext
        {
            ChannelPhone = resolvedPhone
        });

        var session = new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            BusinessToday = clockSnapshot.Today,
            BusinessNow = clockSnapshot.Now,
            ChannelPhone = resolvedPhone,
            EscalationContacts = config.EscalationContacts,
            Config = config,
            ConversationState = state,
            Conversation = conversation,
            Facts = mutableFacts
        };

        foreach (var loader in _packContextLoaders)
        {
            if (!config.CapabilityPacks.Contains(loader.PackId, StringComparer.OrdinalIgnoreCase))
                continue;
            await loader.LoadAsync(session, ct);
        }

        _logger.LogInformation("Conv {C}: session facts after hydrate [{Facts}]", conversationId,
            string.Join(',', session.Facts.Keys));

        return session;
    }

    private static string ResolveEngagementKey(string userNumber, IEnumerable<Domain.Entities.Message> history)
    {
        return history.Any(m =>
            m.Sender.Equals("bot", StringComparison.OrdinalIgnoreCase)
            || m.Sender.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            ? "continuingSession"
            : "firstEver";
    }

    private static bool IsKillSwitchPhrase(string message, IReadOnlyList<string> phrases)
    {
        if (phrases.Count == 0) return false;
        var lower = message.ToLowerInvariant();
        return phrases.Any(p => lower.Contains(p.ToLowerInvariant()));
    }

    private static string SanitizeInput(string message, int maxChars) =>
        message.Length > maxChars ? message[..maxChars].Trim() : message.Trim();
}
