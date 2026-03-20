using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Routes by conditions evaluated in three phases:
///   Phase 1: Flag/variable-based routes (zero LLM cost)
///   Phase 2: Already-detected intents from agent ReRoute (zero LLM cost)
///   Phase 3: LLM classification using flow-level RoutingIntents
///
/// Config:
///   routes: [{ "when": "intent_key" | object, "port": "portId" }]
///   defaultPort: string
///   classification: { instructions: string } — optional, for LLM classification guidance
/// </summary>
public class IntentionRouterNodeHandler : INodeHandler
{
    private readonly ILLMAdapter _llm;
    private readonly ILogger<IntentionRouterNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.IntentionRouter;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public IntentionRouterNodeHandler(ILLMAdapter llm, ILogger<IntentionRouterNodeHandler> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;
        var defaultPort = config.TryGetProperty("defaultPort", out var dp) ? dp.GetString() : null;

        if (!config.TryGetProperty("routes", out var routesProp) || routesProp.ValueKind != JsonValueKind.Array)
            return NodeExecutionResult.Advance(defaultPort);

        var routes = routesProp.EnumerateArray().ToList();

        // ── Phase 1: Flag/variable-based routes (zero cost) ─────────────────────
        foreach (var route in routes)
        {
            if (!route.TryGetProperty("when", out var whenEl)) continue;
            var port = route.TryGetProperty("port", out var p) ? p.GetString() : null;
            if (port == null) continue;

            if (whenEl.ValueKind == JsonValueKind.Object && IsStructuredCondition(whenEl))
            {
                if (EvaluateStructuredWhen(whenEl, ctx))
                {
                    _logger.LogDebug("Router: Phase 1 (flag/variable) matched route to port '{Port}'", port);
                    return NodeExecutionResult.Advance(port);
                }
            }
        }

        // ── Phase 2: Already-detected intents from agent ReRoute (zero cost) ────
        if (ctx.DetectedIntentions.Any(kv => kv.Value))
        {
            foreach (var route in routes)
            {
                if (!route.TryGetProperty("when", out var whenEl)) continue;
                var port = route.TryGetProperty("port", out var p) ? p.GetString() : null;
                if (port == null) continue;

                if (IsIntentRoute(whenEl, out var intentKey) && ctx.IsIntentionDetected(intentKey))
                {
                    _logger.LogDebug("Router: Phase 2 (pre-detected) matched intent '{Intent}' to port '{Port}'", intentKey, port);
                    return NodeExecutionResult.Advance(port);
                }
            }
        }

        // ── Phase 3: LLM classification using RoutingIntents ────────────────────
        var routingIntents = ctx.FlowDefinition.RoutingIntents;
        if (routingIntents.Count > 0 && !string.IsNullOrWhiteSpace(ctx.UserMessage))
        {
            _logger.LogDebug("Router: Phase 3 — running LLM classification for {Count} routing intents", routingIntents.Count);
            var detected = await ClassifyIntentAsync(node, routingIntents, ctx, ct);

            foreach (var kv in detected)
                ctx.DetectedIntentions[kv.Key] = kv.Value;

            foreach (var route in routes)
            {
                if (!route.TryGetProperty("when", out var whenEl)) continue;
                var port = route.TryGetProperty("port", out var p) ? p.GetString() : null;
                if (port == null) continue;

                if (IsIntentRoute(whenEl, out var intentKey) && ctx.IsIntentionDetected(intentKey))
                {
                    _logger.LogDebug("Router: Phase 3 (LLM) matched intent '{Intent}' to port '{Port}'", intentKey, port);
                    return NodeExecutionResult.Advance(port);
                }
            }
        }

        _logger.LogDebug("Router: No route matched — using defaultPort '{Port}'", defaultPort);
        return NodeExecutionResult.Advance(defaultPort);
    }

    private static bool IsStructuredCondition(JsonElement whenEl)
    {
        var type = whenEl.TryGetProperty("type", out var t) ? t.GetString() : null;
        return type is "flag_true" or "flag_false" or "variable_filled"
            or "variable_not_null" or "variable_equals" or "variable_equals_var";
    }

    private static bool IsIntentRoute(JsonElement whenEl, out string intentKey)
    {
        intentKey = string.Empty;

        if (whenEl.ValueKind == JsonValueKind.String)
        {
            intentKey = whenEl.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(intentKey);
        }

        if (whenEl.ValueKind == JsonValueKind.Object)
        {
            var type = whenEl.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is "intention" or "intent")
            {
                intentKey = whenEl.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty;
                return !string.IsNullOrEmpty(intentKey);
            }
        }

        return false;
    }

    private async Task<Dictionary<string, bool>> ClassifyIntentAsync(
        FlowNode node,
        List<FlowRoutingIntent> intents,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var result = intents.ToDictionary(i => i.Key, _ => false);

        var prompt = BuildClassificationPrompt(node, intents, ctx);

        var messages = new List<LLMMessage> { new() { Role = LLMRole.System, Content = prompt } };

        var historySlice = ctx.State.ConversationHistory
            .TakeLast(ctx.FlowDefinition.EngineSettings.MaxConversationHistoryMessages * 2)
            .ToList();
        foreach (var msg in historySlice)
        {
            var role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? LLMRole.User : LLMRole.Assistant;
            messages.Add(new() { Role = role, Content = msg.Content });
        }

        messages.Add(new() { Role = LLMRole.User, Content = $"---MENSAJE---\n{ctx.UserMessage}\n---MENSAJE---" });

        var request = new LLMRequest
        {
            Temperature = ctx.FlowDefinition.EngineSettings.ExtractionTemperature,
            MaxTokens = 200,
            Messages = messages
        };

        try
        {
            var response = await _llm.SendWithJsonModeAsync(request, ct);
            if (!response.Success || string.IsNullOrEmpty(response.Content))
                return result;

            using var doc = JsonDocument.Parse(response.Content);
            if (doc.RootElement.TryGetProperty("intentions", out var intentionsEl)
                && intentionsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in intentionsEl.EnumerateObject())
                {
                    if (result.ContainsKey(prop.Name))
                        result[prop.Name] = prop.Value.ValueKind == JsonValueKind.True;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router LLM classification failed — all intents default to false");
        }

        return result;
    }

    private static string BuildClassificationPrompt(
        FlowNode node,
        List<FlowRoutingIntent> intents,
        FlowTurnContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CLASIFICACIÓN DE INTENCIÓN — JSON MODE");
        sb.AppendLine("Responde SOLO con JSON válido. Sin texto fuera del JSON.");
        sb.AppendLine();
        sb.AppendLine("## Formato de respuesta:");
        sb.AppendLine("{");
        sb.AppendLine("  \"intentions\": {");
        sb.AppendLine(string.Join(",\n", intents.Select(i => $"    \"{i.Key}\": false")));
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("## Intenciones posibles:");
        foreach (var i in intents)
        {
            sb.Append($"- {i.Key}: {i.Description}");
            if (i.Examples.Count > 0)
                sb.Append($" — Ej: {string.Join(", ", i.Examples.Take(3).Select(e => $"\"{e}\""))}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## Reglas:");
        sb.AppendLine("- Analiza SOLO el mensaje delimitado con ---MENSAJE---.");
        sb.AppendLine("- Marca como true la intención que mejor describe lo que el usuario quiere.");
        sb.AppendLine("- Si ninguna aplica claramente, deja todas en false.");
        sb.AppendLine("- Solo UNA intención debería ser true (la más relevante).");

        var classificationInstructions = node.Config.TryGetProperty("classification", out var cl)
            && cl.TryGetProperty("instructions", out var ci)
            ? ci.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(classificationInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("## Instrucciones adicionales:");
            sb.AppendLine(classificationInstructions);
        }

        return sb.ToString();
    }

    private static bool EvaluateStructuredWhen(JsonElement when, FlowTurnContext ctx)
    {
        var type = when.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(type)) return false;

        var state = ctx.State;
        return type.ToLowerInvariant() switch
        {
            "intention" or "intent" =>
                when.TryGetProperty("key", out var k) && ctx.IsIntentionDetected(k.GetString() ?? ""),

            "flag_true" =>
                when.TryGetProperty("flag", out var f) && state.GetFlag(f.GetString() ?? ""),

            "flag_false" =>
                when.TryGetProperty("flag", out var f2) && !state.GetFlag(f2.GetString() ?? ""),

            "variable_filled" or "variable_not_null" =>
                when.TryGetProperty("variable", out var v) &&
                !string.IsNullOrEmpty(state.GetVariable(v.GetString() ?? "")),

            "variable_equals" =>
                when.TryGetProperty("variable", out var ve) &&
                when.TryGetProperty("value", out var val) &&
                string.Equals(
                    state.GetVariable(ve.GetString() ?? ""),
                    val.GetString(),
                    StringComparison.OrdinalIgnoreCase),

            "variable_equals_var" =>
                when.TryGetProperty("left", out var left) &&
                when.TryGetProperty("right", out var right) &&
                string.Equals(
                    state.GetVariable(left.GetString() ?? ""),
                    state.GetVariable(right.GetString() ?? ""),
                    StringComparison.OrdinalIgnoreCase),

            _ => false
        };
    }
}
