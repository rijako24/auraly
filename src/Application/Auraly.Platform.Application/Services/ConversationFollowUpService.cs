using System.Text.Json;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Runtime;
using Auraly.Platform.Application.Billing;
using Auraly.Platform.Application.LLM;
using Auraly.Platform.Application.StateManagement;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Models;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public interface IConversationFollowUpService
{
    Task CancelPendingAsync(Guid conversationId, CancellationToken ct = default);

    Task ScheduleAfterDeliveredTurnAsync(
        Guid agentId,
        Conversation conversation,
        DateTime waitingSinceUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Owns the single customer-reply wait stored on ConversationState and processes
/// due waits. It deliberately does not create a second scheduling aggregate.
/// </summary>
public sealed class ConversationFollowUpService : IConversationFollowUpService, ITimedProcess
{
    public const string ProcessName = "conversation_follow_up";
    private const string Provider = "whatsapp";
    private const int BatchSize = 50;
    private const int HistorySize = 8;
    private const int StateMutationAttempts = 3;

    private readonly IConversationStateRepository _stateRepository;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly IInboundMessageDeduplicationService _inboundReceipts;
    private readonly IAgentConfigProvider _configProvider;
    private readonly IConversationFactsService _facts;
    private readonly IDeterministicResponseRenderer _renderer;
    private readonly IMessageSequenceResolver _sequences;
    private readonly IOutboundMessageDispatcher _dispatcher;
    private readonly IBusinessClock _businessClock;
    private readonly IWorkingHoursService _workingHours;
    private readonly IUsageBillingService _usageBilling;
    private readonly ILogger<ConversationFollowUpService> _logger;

    public ConversationFollowUpService(
        IConversationStateRepository stateRepository,
        IConversationStateManager stateManager,
        IConversationService conversations,
        IMessageService messages,
        IInboundMessageDeduplicationService inboundReceipts,
        IAgentConfigProvider configProvider,
        IConversationFactsService facts,
        IDeterministicResponseRenderer renderer,
        IMessageSequenceResolver sequences,
        IOutboundMessageDispatcher dispatcher,
        IBusinessClock businessClock,
        IWorkingHoursService workingHours,
        IUsageBillingService usageBilling,
        ILogger<ConversationFollowUpService> logger)
    {
        _stateRepository = stateRepository;
        _stateManager = stateManager;
        _conversations = conversations;
        _messages = messages;
        _inboundReceipts = inboundReceipts;
        _configProvider = configProvider;
        _facts = facts;
        _renderer = renderer;
        _sequences = sequences;
        _dispatcher = dispatcher;
        _businessClock = businessClock;
        _workingHours = workingHours;
        _usageBilling = usageBilling;
        _logger = logger;
    }

    public string Name => ProcessName;

    public async Task ScheduleAfterDeliveredTurnAsync(
        Guid agentId,
        Conversation conversation,
        DateTime waitingSinceUtc,
        CancellationToken ct = default)
    {
        var config = await _configProvider.GetConfigAsync(agentId, ct);
        if (!config.ConversationFollowUp.Enabled)
            return;

        if (await HasInboundReplyAsync(conversation, waitingSinceUtc, ct))
            return;

        var history = await _messages.GetRecentConversationHistoryAsync(
            conversation.ConversationId,
            1,
            ct);
        var source = history.LastOrDefault();
        if (source is null
            || !IsBot(source)
            || source.Timestamp < waitingSinceUtc)
        {
            _logger.LogWarning(
                "Conversation follow-up not scheduled for {ConversationId}: delivered bot message was not found in history.",
                conversation.ConversationId);
            return;
        }

        for (var attempt = 1; attempt <= StateMutationAttempts; attempt++)
        {
            var state = await _stateManager.GetStateByConversationIdAsync(conversation.ConversationId, ct);
            if (state is null || state.Owner != ConversationOwner.Bot)
                return;
            if (await HasInboundReplyAsync(conversation, waitingSinceUtc, ct))
                return;

            state.CustomerReplyExpectationVersion++;
            state.PendingCustomerReply = new PendingCustomerReply
            {
                Version = state.CustomerReplyExpectationVersion,
                AgentId = agentId,
                RequestGeneration = state.RequestGeneration,
                FlowId = state.ActiveFlowId ?? string.Empty,
                StageId = state.ActiveStageId ?? string.Empty,
                SourceMessageId = source.MessageId,
                WaitingSinceUtc = waitingSinceUtc
            };
            state.FollowUpDueAtUtc = DateTime.UtcNow.AddMinutes(config.ConversationFollowUp.DelayMinutes);

            try
            {
                await _stateManager.SaveStateAsync(conversation.ConversationId, state, ct);
                return;
            }
            catch (InvalidOperationException) when (attempt < StateMutationAttempts)
            {
                // A concurrent inbound turn or automation changed the cursor; retry from fresh state.
            }
        }
    }

    public async Task CancelPendingAsync(Guid conversationId, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= StateMutationAttempts; attempt++)
        {
            var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
            if (state is null
                || (state.PendingCustomerReply is null && state.FollowUpDueAtUtc is null))
                return;

            state.PendingCustomerReply = null;
            state.FollowUpDueAtUtc = null;
            try
            {
                await _stateManager.SaveStateAsync(conversationId, state, ct);
                return;
            }
            catch (InvalidOperationException) when (attempt < StateMutationAttempts)
            {
                // Retry so a customer reply always wins over a concurrent due scan.
            }
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var due = await _stateRepository.GetDueFollowUpConversationIdsAsync(
            DateTime.UtcNow,
            BatchSize,
            ct);
        foreach (var conversationId in due)
        {
            try
            {
                await ProcessDueAsync(conversationId, ct);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Conversation follow-up failed for {ConversationId}.",
                    conversationId);
            }
        }
    }

    private async Task ProcessDueAsync(Guid conversationId, CancellationToken ct)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        var pending = state?.PendingCustomerReply;
        if (state is null
            || pending is null
            || state.FollowUpDueAtUtc is null
            || state.FollowUpDueAtUtc > DateTime.UtcNow)
            return;

        if (!IsStateStillWaiting(state, pending))
        {
            await MarkTerminalAsync(conversationId, pending.Version, "conversation_state_changed", ct);
            return;
        }

        pending.ClaimedAtUtc = DateTime.UtcNow;
        state.FollowUpDueAtUtc = null;
        try
        {
            await _stateManager.SaveStateAsync(conversationId, state, ct);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var conversation = await _conversations.GetConversationByIdAsync(conversationId);
        if (conversation is null || conversation.Status != ConversationLifecycleStatus.Active)
        {
            await MarkTerminalAsync(conversationId, pending.Version, "conversation_not_active", ct);
            return;
        }

        var config = await _configProvider.GetConfigAsync(pending.AgentId, ct);
        if (!config.ConversationFollowUp.Enabled)
        {
            await MarkTerminalAsync(conversationId, pending.Version, "configuration_disabled", ct);
            return;
        }

        var flow = config.Flows.FirstOrDefault(candidate =>
            candidate.Id.Equals(pending.FlowId, StringComparison.OrdinalIgnoreCase));
        var stage = flow?.Stages.FirstOrDefault(candidate =>
            candidate.Id.Equals(pending.StageId, StringComparison.OrdinalIgnoreCase));
        if (stage is null)
        {
            await MarkTerminalAsync(conversationId, pending.Version, "configured_stage_not_found", ct);
            return;
        }
        if (!stage.AwaitCustomerReply)
        {
            await MarkTerminalAsync(conversationId, pending.Version, "stage_follow_up_disabled", ct);
            return;
        }

        var nextOpening = await ResolveNextOpeningAsync(config, ct);
        if (nextOpening.HasValue)
        {
            await DeferAsync(conversationId, pending.Version, nextOpening.Value, ct);
            return;
        }

        var history = await _messages.GetRecentConversationHistoryAsync(conversationId, HistorySize, ct);
        if (!await IsStillWaitingAsync(conversation, pending, history, ct))
        {
            await MarkTerminalAsync(conversationId, pending.Version, "customer_or_conversation_moved", ct);
            return;
        }

        var usageGate = await _usageBilling.CanProcessAsync(config.BusinessId, ct);
        if (!usageGate.IsAllowed)
        {
            await MarkTerminalAsync(conversationId, pending.Version, $"usage_blocked:{usageGate.Code}", ct);
            return;
        }

        var facts = await _facts.GetAllAsync(conversationId, ct);
        IReadOnlyList<OutboundMessage> outbound = [];
        if (!string.IsNullOrWhiteSpace(config.ConversationFollowUp.Guidance))
        {
            var rendered = await _renderer.RenderFollowUpAsync(
                new DeterministicFollowUpRequest(
                    config,
                    stage,
                    facts,
                    history.Last().MessageText,
                    ToChatHistory(history)),
                ct);
            await ChargeRenderingAsync(config, conversationId, rendered, ct);
            if (rendered.Success && !string.IsNullOrWhiteSpace(rendered.Text))
                outbound = [new OutboundMessage(rendered.Text, null)];
        }

        if (outbound.Count == 0 && !string.IsNullOrWhiteSpace(config.ConversationFollowUp.FallbackSequence))
        {
            outbound = await _sequences.ResolveAsync(
                config.BusinessId,
                config.ConversationFollowUp.FallbackSequence,
                config.MessageSequences,
                new MessageSequenceContext { Custom = facts },
                ct);
        }

        if (outbound.Count == 0)
        {
            await MarkTerminalAsync(conversationId, pending.Version, "follow_up_rendering_failed", ct);
            return;
        }

        var latestHistory = await _messages.GetRecentConversationHistoryAsync(conversationId, 1, ct);
        if (!await IsStillWaitingAsync(conversation, pending, latestHistory, ct))
        {
            await MarkTerminalAsync(conversationId, pending.Version, "customer_or_conversation_moved", ct);
            return;
        }

        await _dispatcher.SendAllAsync(
            conversation.BusinessId,
            conversation.UserNumber,
            outbound,
            conversationId,
            ct,
            throwOnFailure: true);
        await MarkSentAsync(conversationId, pending.Version, ct);
    }

    private async Task<bool> IsStillWaitingAsync(
        Conversation conversation,
        PendingCustomerReply pending,
        IReadOnlyList<Message> history,
        CancellationToken ct)
    {
        var currentState = await _stateManager.GetStateByConversationIdAsync(
            conversation.ConversationId,
            ct);
        var currentPending = currentState?.PendingCustomerReply;
        if (currentState is null
            || currentPending is null
            || currentPending.Version != pending.Version
            || currentPending.ClaimedAtUtc is null
            || !IsStateStillWaiting(currentState, currentPending))
            return false;

        var latest = history.LastOrDefault();
        return latest is not null
            && latest.MessageId == pending.SourceMessageId
            && IsBot(latest)
            && !await HasInboundReplyAsync(conversation, pending.WaitingSinceUtc, ct);
    }

    private Task<bool> HasInboundReplyAsync(
        Conversation conversation,
        DateTime waitingSinceUtc,
        CancellationToken ct) =>
        _inboundReceipts.HasConversationMessageReceivedAfterAsync(
            conversation.BusinessId,
            Provider,
            conversation.UserNumber,
            waitingSinceUtc,
            ct);

    private async Task<DateTime?> ResolveNextOpeningAsync(AgentConfig config, CancellationToken ct)
    {
        if (!config.ConversationFollowUp.RespectOperatingHours
            || !config.OperatingHours.Enforce)
            return null;

        var clock = await _businessClock.GetSnapshotAsync(config.BusinessId, ct);
        for (var offset = 0; offset <= 14; offset++)
        {
            var date = clock.Today.AddDays(offset);
            var blocks = await _workingHours.GetEffectiveBusinessWorkingHoursAsync(
                config.BusinessId,
                date,
                ct);
            foreach (var block in blocks.Where(candidate => candidate.IsValid()).OrderBy(candidate => candidate.OpenTime))
            {
                if (offset == 0
                    && clock.Now.TimeOfDay >= block.OpenTime
                    && clock.Now.TimeOfDay < block.CloseTime)
                    return null;
                if (offset == 0 && block.OpenTime <= clock.Now.TimeOfDay)
                    continue;

                var localOpening = DateTime.SpecifyKind(
                    date.ToDateTime(TimeOnly.FromTimeSpan(block.OpenTime)),
                    DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(localOpening, clock.TimeZone);
            }
        }

        // No configured window was found. Retry later instead of violating the policy.
        return clock.Now.UtcDateTime.AddHours(24);
    }

    private async Task DeferAsync(
        Guid conversationId,
        long pendingVersion,
        DateTime dueAtUtc,
        CancellationToken ct)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        if (state?.PendingCustomerReply?.Version != pendingVersion)
            return;
        state.PendingCustomerReply.ClaimedAtUtc = null;
        state.FollowUpDueAtUtc = dueAtUtc;
        await _stateManager.SaveStateAsync(conversationId, state, ct);
    }

    private async Task MarkSentAsync(Guid conversationId, long pendingVersion, CancellationToken ct)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        if (state?.PendingCustomerReply?.Version != pendingVersion)
            return;
        state.PendingCustomerReply.FollowUpSentAtUtc = DateTime.UtcNow;
        state.PendingCustomerReply.TerminalReason = "sent";
        state.FollowUpDueAtUtc = null;
        await _stateManager.SaveStateAsync(conversationId, state, ct);
    }

    private async Task MarkTerminalAsync(
        Guid conversationId,
        long pendingVersion,
        string reason,
        CancellationToken ct)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        if (state?.PendingCustomerReply?.Version != pendingVersion)
            return;
        state.PendingCustomerReply.TerminalReason = reason;
        state.FollowUpDueAtUtc = null;
        await _stateManager.SaveStateAsync(conversationId, state, ct);
    }

    private Task ChargeRenderingAsync(
        AgentConfig config,
        Guid conversationId,
        DeterministicRenderedResponse rendered,
        CancellationToken ct) =>
        _usageBilling.ChargeAsync(new UsageChargeRequest(
            config.BusinessId,
            config.AgentId,
            conversationId,
            null,
            UsageOperationType.AgentTurn,
            rendered.PromptTokens,
            rendered.CompletionTokens,
            0,
            0,
            config.Model,
            MetadataJson: JsonSerializer.Serialize(new { engine = "conversation_follow_up" })), ct);

    private static bool IsStateStillWaiting(ConversationState state, PendingCustomerReply pending) =>
        state.Owner == ConversationOwner.Bot
        && pending.FollowUpSentAtUtc is null
        && state.RequestGeneration == pending.RequestGeneration
        && string.Equals(state.ActiveFlowId, pending.FlowId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(state.ActiveStageId, pending.StageId, StringComparison.OrdinalIgnoreCase);

    private static bool IsBot(Message message) =>
        message.Sender.Equals("bot", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ChatMessage> ToChatHistory(IReadOnlyList<Message> history) =>
        history.Select(message =>
                message.Sender.Equals("user", StringComparison.OrdinalIgnoreCase)
                    ? ChatMessage.User(message.MessageText)
                    : ChatMessage.Assistant(message.MessageText))
            .ToList();
}
