using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
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
    private readonly IFlowStageDetector _flowStageDetector;

    public ToolCapabilityGate(IGuardEvaluator guardEvaluator, IFlowStageDetector flowStageDetector)
    {
        _guardEvaluator = guardEvaluator;
        _flowStageDetector = flowStageDetector;
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
                null));
        }

        var runtimeActive = !ReferenceEquals(ctx.RuntimeDecision, Runtime.FlowRuntimeDecision.Empty);
        if (runtimeActive && !ctx.RuntimeDecision.IsToolAllowedByRuntime(tool))
        {
            return Task.FromResult(new GateResult(
                false,
                "tool_not_allowed_in_runtime_state",
                "Tool is not allowed in the current runtime state.",
                null));
        }

        var stageResult = EvaluateStageAllowedTools(tool, config, ctx);
        if (stageResult is not null)
            return Task.FromResult(stageResult);

        var evaluation = _guardEvaluator.EvaluateTool(tool, config, ctx, arguments);
        if (evaluation.IsAvailable)
            return Task.FromResult(new GateResult(true, null, null, null));

        return Task.FromResult(new GateResult(
            false,
            "precondition_failed",
            evaluation.BlockReason,
            evaluation.Remediation));
    }

    private GateResult? EvaluateStageAllowedTools(IAgentTool tool, AgentConfig config, AgentToolContext ctx)
    {
        if (config.Flow.Stages.Count == 0)
            return null;

        var stage = _flowStageDetector.DetectCurrentStage(config.Flow, ctx);
        if (stage is null || stage.AllowedTools.Count == 0)
            return null;

        var runtimeActive = !ReferenceEquals(ctx.RuntimeDecision, Runtime.FlowRuntimeDecision.Empty);
        if (runtimeActive && ctx.RuntimeDecision.ExtraAllowedToolNames.Contains(tool.Name))
            return null;

        if (ToolFlowScope.IsAllowedInScope(tool.Name, config, stage, ctx.RuntimeDecision))
            return null;

        var remediation = !string.IsNullOrWhiteSpace(stage.ConversationGuidance)
            ? stage.ConversationGuidance.Trim()
            : null;

        return new GateResult(
            false,
            "stage_action_pending",
            $"Etapa '{stage.Id}': {stage.Goal}",
            remediation);
    }

}
