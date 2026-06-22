using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Domain.Catalog;
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
                "Retry the turn or escalate to human."));
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

        if (stage.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            return null;

        if (IsOrderModificationTool(tool) && IsOrderModificationIntent(ctx.LatestUserMessage))
            return null;

        if (IsAllowedByGlobalAction(tool, config))
            return null;

        var remediation = !string.IsNullOrWhiteSpace(stage.Hint)
            ? stage.Hint.Trim()
            : $"Continua con la etapa actual usando una de estas acciones: {string.Join(", ", stage.AllowedTools)}.";

        return new GateResult(
            false,
            "stage_action_pending",
            $"Etapa '{stage.Id}': {stage.Goal}",
            remediation);
    }

    private static bool IsAllowedByGlobalAction(IAgentTool tool, AgentConfig config) =>
        config.GlobalActions.Any(action =>
            action.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase));

    private static bool IsOrderModificationTool(IAgentTool tool) =>
        tool.Name.Equals("search_products", StringComparison.OrdinalIgnoreCase)
        || tool.Name.Equals("add_order_item", StringComparison.OrdinalIgnoreCase)
        || tool.Name.Equals("remove_order_item", StringComparison.OrdinalIgnoreCase)
        || tool.Name.Equals("get_order_draft", StringComparison.OrdinalIgnoreCase)
        || tool.Name.Equals("prepare_order_checkout", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrderModificationIntent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var normalized = CatalogSearchText.NormalizeCompact(message);
        string[] triggers =
        [
            "agregar",
            "anadir",
            "adicionar",
            "otroproducto",
            "quitar",
            "sacar",
            "remover",
            "eliminar",
            "borrar",
            "modificar",
            "cambiar",
            "pedido",
            "carrito",
            "opciones"
        ];

        return triggers.Any(normalized.Contains);
    }
}
