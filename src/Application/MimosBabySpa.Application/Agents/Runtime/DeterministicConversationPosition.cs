using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Runtime;

public static class DeterministicConversationPosition
{
    public static AgentFlowDefinition ResolveFlow(AgentConfig config, ConversationState state) =>
        AgentFlowCatalog.Find(config, state.ActiveFlowId)
        ?? AgentFlowCatalog.PrimaryFlow(config)
        ?? throw new InvalidOperationException($"Agent '{config.AgentId}' has no compiled flow.");

    public static AgentFlowStage ResolveStage(
        AgentFlowDefinition flow,
        ConversationState state,
        IReadOnlyDictionary<string, string> facts)
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
            if (!IsComplete(stage, facts))
                return stage;
        }

        return flow.Stages[configuredIndex];
    }

    private static bool IsComplete(AgentFlowStage stage, IReadOnlyDictionary<string, string> facts) =>
        stage.AdvanceWhenFacts.Count > 0
        && stage.AdvanceWhenFacts.All(key =>
            facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));
}
