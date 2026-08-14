namespace Auraly.Platform.Application.Agents.Runtime;

public sealed record FlowRouteDecision(
    string ActiveFlowId,
    string Decision,
    string Reason,
    double Confidence,
    bool IsPrimaryFlow)
{
    public static FlowRouteDecision Primary(string flowId, string reason = "primary_flow") =>
        new(flowId, "primary_flow", reason, 1.0, true);
}
