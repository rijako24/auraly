using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Gating;

public sealed record GateResult(bool IsAllowed, string? Code, string? Reason, string? Remediation);

public interface IToolCapabilityGate
{
    Task<GateResult> EvaluateAsync(
        IAgentTool tool,
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken ct);
}

public sealed class ToolCapabilityGate : IToolCapabilityGate
{
    private readonly IGuardEvaluator _guardEvaluator;

    public ToolCapabilityGate(IGuardEvaluator guardEvaluator)
    {
        _guardEvaluator = guardEvaluator;
    }

    public Task<GateResult> EvaluateAsync(
        IAgentTool tool,
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        var config = ctx.Config;
        if (config is null)
        {
            return Task.FromResult(new GateResult(
                false,
                "agent_config_missing",
                "Agent configuration is not available for tool gating.",
                "Retry the turn or escalate to human."));
        }

        var evaluation = _guardEvaluator.EvaluateTool(tool, config, ctx, arguments);
        if (evaluation.IsAvailable)
            return Task.FromResult(new GateResult(true, null, null, null));

        return Task.FromResult(new GateResult(
            false,
            "precondition_failed",
            evaluation.BlockReason,
            evaluation.Remediation));
    }
}
