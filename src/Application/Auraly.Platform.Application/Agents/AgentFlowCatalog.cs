using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents;

internal static class AgentFlowCatalog
{
    public static IReadOnlyList<AgentFlowDefinition> EffectiveFlows(AgentConfig config) =>
        config.Flows;

    public static string ResolvePrimaryFlowId(AgentConfig config) =>
        EffectiveFlows(config).FirstOrDefault(flow => IsPrimary(flow) && !string.IsNullOrWhiteSpace(flow.Id))?.Id
        ?? EffectiveFlows(config).FirstOrDefault(flow => !string.IsNullOrWhiteSpace(flow.Id))?.Id
        ?? string.Empty;

    public static AgentFlowDefinition? Find(AgentConfig config, string? flowId)
    {
        if (string.IsNullOrWhiteSpace(flowId))
            return null;

        return EffectiveFlows(config).FirstOrDefault(flow =>
            flow.Id.Equals(flowId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static AgentFlowDefinition? PrimaryFlow(AgentConfig config) =>
        Find(config, ResolvePrimaryFlowId(config))
        ?? EffectiveFlows(config).FirstOrDefault(flow => IsPrimary(flow))
        ?? EffectiveFlows(config).FirstOrDefault();

    public static bool IsPrimary(AgentFlowDefinition flow) =>
        string.IsNullOrWhiteSpace(flow.Type)
        || flow.Type.Equals(FlowTypes.Primary, StringComparison.OrdinalIgnoreCase);

    public static bool IsSecondary(AgentFlowDefinition flow) =>
        flow.Type.Equals(FlowTypes.Secondary, StringComparison.OrdinalIgnoreCase);

    public static TimeSpan ResolveTtl(AgentFlowDefinition flow) =>
        TimeSpan.FromSeconds(flow.TtlSeconds.GetValueOrDefault(FlowConventions.SecondaryFlowDefaultTtlSeconds));
}
