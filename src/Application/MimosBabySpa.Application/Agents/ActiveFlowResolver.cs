using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents;

internal static class ActiveFlowResolver
{
    public static AgentFlowDefinition Resolve(AgentConfig config, AgentToolContext session) =>
        AgentFlowCatalog.Find(config, session.RuntimeDecision.Route.ActiveFlowId)
        ?? AgentFlowCatalog.PrimaryFlow(config)
        ?? config.Flow;
}
