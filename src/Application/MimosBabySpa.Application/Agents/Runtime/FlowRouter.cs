using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class FlowRouter : IFlowRouter
{
    private readonly IChatClient _chatClient;
    private readonly IFlowStageDetector _stageDetector;

    public FlowRouter(IChatClient chatClient, IFlowStageDetector stageDetector)
    {
        _chatClient = chatClient;
        _stageDetector = stageDetector;
    }

    public async Task<FlowRouteDecision> RouteAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct)
    {
        var primaryFlow = AgentFlowCatalog.PrimaryFlow(config);
        if (primaryFlow is null)
            return FlowRouteDecision.Primary(string.Empty, "no_configured_flows");

        var primaryFlowId = primaryFlow.Id;
        var active = ActiveFlowRuntimeState.Get(session.ConversationState);
        var now = DateTime.UtcNow;
        string? activeSecondaryFlowId = null;


        if (active is not null)
        {
            var activeFlow = AgentFlowCatalog.Find(config, active.FlowId);
            if (activeFlow is not null
                && AgentFlowCatalog.IsSecondary(activeFlow)
                && active.ExpiresAtUtc > now)
            {
                activeSecondaryFlowId = activeFlow.Id;
                if (MessageCanContinueAwaitingFacts(config, activeFlow, session, userMessage))
                {
                    var ttl = AgentFlowCatalog.ResolveTtl(activeFlow);
                    ActiveFlowRuntimeState.Set(
                        session.ConversationState,
                        activeFlow.Id,
                        now,
                        ttl,
                        "continue_secondary_flow",
                        "message_matches_awaited_fact_shape");

                    return new FlowRouteDecision(activeFlow.Id, "continue_secondary_flow", "message_matches_awaited_fact_shape", 1.0, false);
                }
            }
            else
            {
                ActiveFlowRuntimeState.Clear(session.ConversationState);
            }
        }

        if (HasOpenPrimaryRequest(config, primaryFlow, session))
        {
            ActiveFlowRuntimeState.Clear(session.ConversationState);
            return FlowRouteDecision.Primary(primaryFlowId, "open_primary_request");
        }

        var proposed = await ClassifyFlowAsync(config, primaryFlowId, activeSecondaryFlowId, session, userMessage, ct);
        if (proposed is null)
            return FlowRouteDecision.Primary(primaryFlowId, "router_unavailable_or_invalid");

        var proposedFlow = AgentFlowCatalog.Find(config, proposed.FlowId);
        if (proposedFlow is null)
            return FlowRouteDecision.Primary(primaryFlowId, "router_selected_unknown_flow");

        if (AgentFlowCatalog.IsSecondary(proposedFlow)
            && proposed.Confidence >= FlowConventions.SecondaryFlowActivationThreshold)
        {
            var ttl = AgentFlowCatalog.ResolveTtl(proposedFlow);
            ActiveFlowRuntimeState.Set(
                session.ConversationState,
                proposedFlow.Id,
                now,
                ttl,
                "start_secondary_flow",
                proposed.Reason);

            return new FlowRouteDecision(proposedFlow.Id, "start_secondary_flow", proposed.Reason, proposed.Confidence, false);
        }

        if (AgentFlowCatalog.IsPrimary(proposedFlow)
            && proposed.Confidence >= FlowConventions.PrimaryFlowActivationThreshold)
        {
            ActiveFlowRuntimeState.Clear(session.ConversationState);
            return new FlowRouteDecision(proposedFlow.Id, "primary_flow", proposed.Reason, proposed.Confidence, true);
        }

        return FlowRouteDecision.Primary(primaryFlowId, "below_activation_threshold");
    }

    private bool MessageCanContinueAwaitingFacts(
        AgentConfig config,
        AgentFlowDefinition activeFlow,
        AgentToolContext session,
        string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var currentStage = _stageDetector.DetectCurrentStage(activeFlow, session);
        if (currentStage is null)
            return false;

        var missing = currentStage.AdvanceWhenFacts
            .Concat(currentStage.Collect)
            .Where(key => !session.Facts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0)
            return false;

        return missing.Any(key => FactValueShapeMatcher.MessageMatchesFactShape(config.FactSchema, key, userMessage));
    }


    private async Task<RouterProposal?> ClassifyFlowAsync(
        AgentConfig config,
        string primaryFlowId,
        string? activeSecondaryFlowId,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct)
    {
        var flows = AgentFlowCatalog.EffectiveFlows(config);
        var candidates = flows
            .Where(flow => !string.IsNullOrWhiteSpace(flow.Id))
            .Select(flow => new
            {
                id = flow.Id,
                type = string.IsNullOrWhiteSpace(flow.Type) ? FlowTypes.Primary : flow.Type,
                routingGuidance = flow.RoutingGuidance
            })
            .ToList();

        if (candidates.Count <= 1)
            return new RouterProposal(primaryFlowId, 1, "single_flow");

        var prompt = JsonSerializer.Serialize(new
        {
            task = "Select the single best flow for the latest user message. Return only compact JSON.",
            primaryFlow = primaryFlowId,
            rule = "If the message does not clearly require a secondary flow, choose the primary flow. Secondary flows are only for already existing completed requests, not open primary requests, pending summaries or pending payments. If an active secondary flow is present, keep it only when the latest message is a clear continuation of that configured flow.",
            activeSecondaryFlow = activeSecondaryFlowId,
            flows = candidates,
            recentConversation = BuildRecentConversation(session),
            latestUserMessage = userMessage,
            schema = new
            {
                flowId = "string",
                confidence = "number from 0 to 1",
                reason = "short string"
            }
        });

        var result = await _chatClient.CompleteAsync(
            [ChatMessage.System(prompt)],
            tools: null,
            options: new ChatCompletionOptions { Temperature = 0, MaxTokens = 180, ForceTextResponse = true },
            cancellationToken: ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Content);
            var root = doc.RootElement;
            var flowId = root.TryGetProperty("flowId", out var flowIdEl) ? flowIdEl.GetString() : null;
            var confidence = root.TryGetProperty("confidence", out var confidenceEl) && confidenceEl.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : 0;
            var reason = root.TryGetProperty("reason", out var reasonEl) ? reasonEl.GetString() : null;

            return string.IsNullOrWhiteSpace(flowId)
                ? null
                : new RouterProposal(flowId.Trim(), Math.Clamp(confidence, 0, 1), reason ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasOpenPrimaryRequest(
        AgentConfig config,
        AgentFlowDefinition primaryFlow,
        AgentToolContext session)
    {
        if (session.ActivePayment is not null)
            return true;

        var requestFactKeys = primaryFlow.Stages
            .SelectMany(stage => stage.Collect.Concat(stage.AdvanceWhenFacts))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => IsRequestScopedUserFact(config, key));

        return requestFactKeys.Any(key =>
            session.Facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));
    }

    private static bool IsRequestScopedUserFact(AgentConfig config, string key)
    {
        var entry = config.FactSchema.FirstOrDefault(fact =>
            fact.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        return entry is not null
               && entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase)
               && entry.EffectiveScope().Equals(FactScopes.Request, StringComparison.OrdinalIgnoreCase);
    }


    private static IReadOnlyList<object> BuildRecentConversation(AgentToolContext session) =>
        session.Conversation.Messages
            .OrderByDescending(message => message.Timestamp)
            .Take(6)
            .OrderBy(message => message.Timestamp)
            .Select(message => new
            {
                sender = message.Sender,
                text = message.MessageText
            })
            .Cast<object>()
            .ToList();
    private sealed record RouterProposal(string FlowId, double Confidence, string Reason);
}
