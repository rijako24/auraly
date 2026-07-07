using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class FlowRuntimeOrchestrator : IFlowRuntimeOrchestrator
{
    private readonly ITurnEventExtractor _eventExtractor;
    private readonly IFlowRuntimeStateResolver _stateResolver;
    private readonly IFlowPolicyEngine _policyEngine;
    private readonly IConversationFactsService _factsService;

    public FlowRuntimeOrchestrator(
        ITurnEventExtractor eventExtractor,
        IFlowRuntimeStateResolver stateResolver,
        IFlowPolicyEngine policyEngine,
        IConversationFactsService factsService)
    {
        _eventExtractor = eventExtractor;
        _stateResolver = stateResolver;
        _policyEngine = policyEngine;
        _factsService = factsService;
    }

    public async Task<FlowRuntimeDecision> ApplyAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct)
    {
        var events = _eventExtractor.Extract(userMessage);
        var state = _stateResolver.Resolve(config, session);
        var decision = _policyEngine.Decide(config, session, state, events);

        foreach (var (key, value) in decision.FactMutations)
        {
            if (session.Facts.TryGetValue(key, out var current)
                && string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var schemaEntry = config.FactSchema.FirstOrDefault(entry =>
                entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

            await _factsService.SetAsync(
                session.ConversationId,
                session.BusinessId,
                key,
                value,
                schemaEntry?.ShouldRememberAcrossRequests() ?? false,
                ct);

            session.Facts[key] = value;
        }

        return decision;
    }
}
