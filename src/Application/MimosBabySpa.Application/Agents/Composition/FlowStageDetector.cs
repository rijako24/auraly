using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed class FlowStageDetector : IFlowStageDetector
{
    public AgentFlowStage? DetectCurrentStage(AgentFlowDefinition flow, AgentToolContext? session)
    {
        if (flow.Stages.Count == 0)
            return null;

        if (!string.Equals(flow.StageDetection, "automatic", StringComparison.OrdinalIgnoreCase))
            return flow.Stages[0];

        foreach (var stage in flow.Stages)
        {
            if (!IsStageComplete(stage, session))
                return stage;
        }

        return flow.Stages[^1];
    }

    private static bool IsStageComplete(AgentFlowStage stage, AgentToolContext? session)
    {
        if (stage.AdvanceWhenFacts.Count == 0)
            return true;

        foreach (var factKey in stage.AdvanceWhenFacts)
        {
            if (!HasFact(session, factKey))
                return false;
        }

        return true;
    }

    private static bool HasFact(AgentToolContext? session, string factKey)
    {
        if (session is null)
            return false;

        if (ConversationFactKeys.Get(session.Facts, factKey) is not null)
            return true;

        return session.Facts.TryGetValue(factKey, out var value) && !string.IsNullOrWhiteSpace(value);
    }
}
