using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed record FlowSelectionContext(
    string? ActiveFlowId,
    bool HasOpenPrimaryRequest);

public interface IDeterministicFlowSelector
{
    FlowRouteDecision Select(
        AgentConfig config,
        TurnPlan plan,
        FlowSelectionContext context);
}

public sealed class DeterministicFlowSelector : IDeterministicFlowSelector
{
    public FlowRouteDecision Select(
        AgentConfig config,
        TurnPlan plan,
        FlowSelectionContext context)
    {
        var primary = AgentFlowCatalog.PrimaryFlow(config);
        if (primary is null)
            return FlowRouteDecision.Primary(string.Empty, "no_configured_flows");

        if (context.HasOpenPrimaryRequest)
            return FlowRouteDecision.Primary(primary.Id, "open_primary_request");

        var candidate = AgentFlowCatalog.Find(config, plan.FlowIntent.CandidateFlow);
        if (candidate is null)
            return FlowRouteDecision.Primary(primary.Id, "unknown_planned_flow");

        var confidence = Math.Clamp(plan.FlowIntent.Confidence, 0, 1);
        if (AgentFlowCatalog.IsSecondary(candidate))
        {
            if (confidence < FlowConventions.SecondaryFlowActivationThreshold)
                return FlowRouteDecision.Primary(primary.Id, "secondary_below_activation_threshold");

            if (string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence))
                return FlowRouteDecision.Primary(primary.Id, "secondary_without_evidence");

            var continuing = candidate.Id.Equals(context.ActiveFlowId, StringComparison.OrdinalIgnoreCase);
            return new FlowRouteDecision(
                candidate.Id,
                continuing ? "continue_secondary_flow" : "start_secondary_flow",
                continuing ? "turn_plan_continues_active_flow" : "turn_plan_selected_secondary_flow",
                confidence,
                false);
        }

        if (confidence < FlowConventions.PrimaryFlowActivationThreshold)
            return FlowRouteDecision.Primary(primary.Id, "primary_below_activation_threshold");

        return new FlowRouteDecision(
            candidate.Id,
            "primary_flow",
            "turn_plan_selected_primary_flow",
            confidence,
            true);
    }
}
