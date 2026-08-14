using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Domain.Models;

namespace Auraly.Platform.Application.Agents.Runtime;

public static class DeterministicConversationPosition
{
    public static AgentFlowDefinition ResolveFlow(AgentConfig config, ConversationState state) =>
        AgentFlowCatalog.Find(config, state.ActiveFlowId)
        ?? AgentFlowCatalog.PrimaryFlow(config)
        ?? throw new InvalidOperationException($"Agent '{config.AgentId}' has no compiled flow.");

    public static bool ExpireSecondaryFlowIfNeeded(
        AgentConfig config,
        ConversationState state,
        DateTime utcNow)
    {
        var active = AgentFlowCatalog.Find(config, state.ActiveFlowId);
        if (active is null || !AgentFlowCatalog.IsSecondary(active))
        {
            state.ActiveFlowExpiresAtUtc = null;
            return false;
        }

        if (state.ActiveFlowExpiresAtUtc is null)
        {
            state.ActiveFlowExpiresAtUtc = utcNow.Add(AgentFlowCatalog.ResolveTtl(active));
            return false;
        }

        if (state.ActiveFlowExpiresAtUtc > utcNow)
            return false;

        state.ActiveFlowId = AgentFlowCatalog.ResolvePrimaryFlowId(config);
        state.ActiveStageId = null;
        state.ActiveFlowExpiresAtUtc = null;
        return true;
    }

    public static void RefreshFlowLease(
        AgentConfig config,
        ConversationState state,
        DateTime utcNow)
    {
        var active = AgentFlowCatalog.Find(config, state.ActiveFlowId);
        state.ActiveFlowExpiresAtUtc = active is not null && AgentFlowCatalog.IsSecondary(active)
            ? utcNow.Add(AgentFlowCatalog.ResolveTtl(active))
            : null;
    }
    public static AgentFlowStage ResolveStage(
        AgentFlowDefinition flow,
        ConversationState state,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyList<FactSchemaEntry> factSchema)
    {
        if (flow.Stages.Count == 0)
            throw new InvalidOperationException($"Flow '{flow.Id}' has no stages.");

        var configuredIndex = flow.Stages
            .Select((stage, index) => new { stage, index })
            .FirstOrDefault(item => item.stage.Id.Equals(state.ActiveStageId, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        for (var index = 0; index < configuredIndex; index++)
        {
            var stage = flow.Stages[index];
            if (!StageAdvanceFactReadiness.IsComplete(stage, facts, factSchema))
                return stage;
        }

        return flow.Stages[configuredIndex];
    }
}

public static class StageAdvanceFactReadiness
{
    public static bool IsComplete(
        AgentFlowStage stage,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyList<FactSchemaEntry> factSchema) =>
        stage.AdvanceWhenFacts.Count > 0
        && stage.AdvanceWhenFacts.All(key => IsSatisfied(key, facts, factSchema));

    public static bool IsSatisfied(
        string key,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyList<FactSchemaEntry> factSchema)
    {
        if (!facts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return false;

        var definition = factSchema.FirstOrDefault(fact =>
            fact.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (definition?.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase) != true)
            return true;

        return bool.TryParse(value, out var parsed) && parsed;
    }
}