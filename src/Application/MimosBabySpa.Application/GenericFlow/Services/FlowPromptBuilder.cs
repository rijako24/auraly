using System.Text;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Builds the system prompt for a flow turn by assembling:
/// 1. Agent prompt sections (sorted by injection point and display order)
/// 2. Auto-injected knowledge sources
/// 3. Current state summary
/// 4. Action results for the current turn (raw service outputs for the LLM)
/// 5. Node-specific instructions
/// </summary>
public class FlowPromptBuilder
{
    private readonly KnowledgeSourceRenderer _ksRenderer;
    private readonly TemplateResolver _templateResolver;

    public FlowPromptBuilder(KnowledgeSourceRenderer ksRenderer, TemplateResolver templateResolver)
    {
        _ksRenderer = ksRenderer;
        _templateResolver = templateResolver;
    }

    public string Build(
        Agent agent,
        FlowNode currentNode,
        FlowTurnContext ctx,
        IEnumerable<KnowledgeSource> nodeKnowledgeSources)
    {
        var sections = agent.PromptSections
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        var autoSources = agent.KnowledgeSources
            .Where(aks => aks.AutoInject && aks.KnowledgeSource.IsActive)
            .OrderBy(aks => aks.DisplayOrder)
            .Select(aks => aks.KnowledgeSource)
            .ToList();

        var sb = new StringBuilder();

        AppendSections(sb, sections, "system_header", ctx);
        AppendSections(sb, sections, "before_instructions", ctx);

        if (autoSources.Count > 0)
        {
            sb.AppendLine(_ksRenderer.RenderMany(autoSources));
        }

        var nodeSpecificSources = nodeKnowledgeSources.ToList();
        if (nodeSpecificSources.Count > 0)
        {
            sb.AppendLine(_ksRenderer.RenderMany(nodeSpecificSources));
        }

        AppendCurrentState(sb, ctx);
        AppendActionResults(sb, ctx);
        AppendSections(sb, sections, "after_instructions", ctx);
        AppendNodeInstructions(sb, currentNode, ctx);
        AppendSections(sb, sections, "context_footer", ctx);

        return sb.ToString().Trim();
    }

    private void AppendSections(
        StringBuilder sb,
        List<AgentPromptSection> sections,
        string injectionPoint,
        FlowTurnContext ctx)
    {
        foreach (var section in sections.Where(s => s.InjectionPoint == injectionPoint))
        {
            if (!string.IsNullOrWhiteSpace(section.Title))
                sb.AppendLine($"# {section.Title}");

            var resolvedContent = _templateResolver.Resolve(section.Content, ctx);
            sb.AppendLine(resolvedContent);
            sb.AppendLine();
        }
    }

    private static void AppendCurrentState(StringBuilder sb, FlowTurnContext ctx)
    {
        var state = ctx.State;
        var flow = ctx.FlowDefinition;

        // ── Session metadata ──────────────────────────────────────────────────────
        // IsFirstMessage: conversation history is empty (no prior turns this session)
        var isFirstMessage = state.ConversationHistory.Count == 0;
        var isReturningCustomer = isFirstMessage && state.PreviousSession != null;

        sb.AppendLine("# CONTEXTO DE SESIÓN");
        sb.AppendLine($"- Turno: {ctx.TurnNumber}");
        sb.AppendLine($"- Primera interacción de la sesión: {(isFirstMessage ? "Sí" : "No")}");
        sb.AppendLine($"- Cliente recurrente (sesión previa): {(isReturningCustomer ? "Sí" : "No")}");

        if (isReturningCustomer && state.PreviousSession != null)
            sb.AppendLine($"- Sesión anterior finalizada: {state.PreviousSession.SessionEndedAt:yyyy-MM-dd HH:mm} UTC");

        sb.AppendLine();

        // ── Collected variables ───────────────────────────────────────────────────
        var collectedVars = flow.Variables
            .Where(v => v.ShowInSummary && state.GetVariable(v.Key) != null)
            .OrderBy(v => v.DisplayOrder)
            .ToList();

        if (collectedVars.Count > 0)
        {
            sb.AppendLine("# DATOS RECOPILADOS");
            foreach (var v in collectedVars)
                sb.AppendLine($"- {v.Label}: {state.GetVariable(v.Key)}");
            sb.AppendLine(
                "(Estos datos ya fueron confirmados. Úsalos como contexto interno pero no los repitas como opciones nuevas ni los vuelvas a ofrecer.)");
            sb.AppendLine();
        }

        var missingRequired = flow.Variables
            .Where(v => v.Required && !v.IsSystemManaged && state.GetVariable(v.Key) == null)
            .OrderBy(v => v.DisplayOrder)
            .ToList();

        if (missingRequired.Count > 0)
        {
            sb.AppendLine("# DATOS PENDIENTES");
            foreach (var v in missingRequired)
                sb.AppendLine($"- {v.Label}");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Surfaces action node outputs from this turn so the LLM can reason about availability,
    /// pricing, etc. without the engine interpreting business meaning.
    /// </summary>
    private static void AppendActionResults(StringBuilder sb, FlowTurnContext ctx)
    {
        var state = ctx.State;
        var actionNodeIds = state.Trace
            .Where(t => t.TurnNumber == ctx.TurnNumber &&
                        string.Equals(t.NodeType, nameof(FlowNodeType.Action), StringComparison.OrdinalIgnoreCase))
            .Select(t => t.NodeId)
            .Distinct()
            .ToList();

        if (actionNodeIds.Count == 0)
            return;

        sb.AppendLine("# RESULTADO DE ACCIONES EJECUTADAS (ESTE TURNO)");
        foreach (var nodeId in actionNodeIds)
        {
            if (!state.ActionResults.TryGetValue(nodeId, out var data) || data.Count == 0)
                continue;

            sb.AppendLine($"## {nodeId}");
            foreach (var (key, value) in data.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (value is null)
                    continue;
                sb.AppendLine($"- {key}: {value}");
            }

            sb.AppendLine();
        }
    }

    private void AppendNodeInstructions(StringBuilder sb, FlowNode node, FlowTurnContext ctx)
    {
        if (!node.Config.TryGetProperty("instructions", out var instrProp)) return;
        var instructions = instrProp.GetString();
        if (string.IsNullOrWhiteSpace(instructions)) return;

        var resolved = _templateResolver.Resolve(instructions, ctx);
        sb.AppendLine("# INSTRUCCIONES PARA ESTA RESPUESTA");
        sb.AppendLine(resolved);
        sb.AppendLine();
    }
}
