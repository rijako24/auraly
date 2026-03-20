using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Extraction service for cluster-node architecture.
/// Each agent invokes extraction with scoped variables and intentions from its own sub-node.
///
/// A single LLM call in JSON mode extracts both field values AND boolean intention flags.
///
/// Prompt assembly:
///   1. JSON output schema (extracted_fields + intentions + ambiguities)
///   2. General extraction rules
///   3. ExtractionInstructions from FlowDefinitionDocument
///   4. Scoped intention definitions from the calling agent
///   5. ServiceCatalog knowledge sources as valid value reference
///   6. Current variable state
///   7. Variable definitions with ExtractionHints
/// </summary>
public class FlowExtractionService
{
    private readonly ILLMAdapter _llm;
    private readonly TemplateResolver _templateResolver;
    private readonly KnowledgeSourceRenderer _ksRenderer;
    private readonly ILogger<FlowExtractionService> _logger;

    public FlowExtractionService(
        ILLMAdapter llm,
        TemplateResolver templateResolver,
        KnowledgeSourceRenderer ksRenderer,
        ILogger<FlowExtractionService> logger)
    {
        _llm = llm;
        _templateResolver = templateResolver;
        _ksRenderer = ksRenderer;
        _logger = logger;
    }

    /// <summary>
    /// Agent-scoped extraction: extracts only the specified variables and intentions.
    /// </summary>
    public async Task<FlowExtractionResult> ExtractAsync(
        string userMessage,
        FlowDefinitionDocument flow,
        FlowExecutionState state,
        IReadOnlyList<FlowConversationMessage> conversationHistory,
        IEnumerable<KnowledgeSource> knowledgeSources,
        FlowEngineSettings settings,
        CancellationToken ct,
        List<FlowVariable> scopedVariables,
        List<FlowIntentionSchema> scopedIntentions)
    {
        if (scopedVariables.Count == 0 && scopedIntentions.Count == 0)
            return new FlowExtractionResult { WasSuccessful = true };

        var resolvedInstructions = string.IsNullOrWhiteSpace(flow.ExtractionInstructions)
            ? null
            : _templateResolver.ResolveStandalone(flow.ExtractionInstructions, settings.DateTimeCulture);

        var prompt = BuildExtractionPrompt(
            scopedVariables, scopedIntentions, state, resolvedInstructions, knowledgeSources, settings);

        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = prompt }
        };

        var historySlice = conversationHistory
            .TakeLast(settings.MaxConversationHistoryMessages * 2)
            .ToList();

        foreach (var msg in historySlice)
        {
            var role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? LLMRole.User : LLMRole.Assistant;
            messages.Add(new() { Role = role, Content = msg.Content });
        }

        messages.Add(new()
        {
            Role = LLMRole.User,
            Content = $"---MENSAJE A ANALIZAR---\n{userMessage}\n---MENSAJE A ANALIZAR---"
        });

        var request = new LLMRequest
        {
            Temperature = settings.ExtractionTemperature,
            MaxTokens = settings.ExtractionMaxTokens,
            Messages = messages
        };

        try
        {
            var response = await _llm.SendWithJsonModeAsync(request, ct);
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                _logger.LogWarning("Extraction LLM call returned no content");
                return FlowExtractionResult.Failed();
            }

            return ParseExtractionResponse(
                response.Content, scopedVariables, scopedIntentions, settings.ExtractionMinConfidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extraction LLM call failed");
            return FlowExtractionResult.Failed();
        }
    }

    /// <summary>
    /// Applies extracted field values to state. Handles:
    /// - Validation (format/range checks)
    /// - onChange rules (only when value changes from non-null)
    /// - Skips resetting variables that were extracted in the same turn
    /// - Resolves gotoNode conflicts to topologically earliest node
    /// </summary>
    public void ApplyExtraction(
        Dictionary<string, string?> extracted,
        FlowDefinitionDocument flow,
        FlowExecutionState state,
        FlowTurnContext ctx,
        IEnumerable<KnowledgeSource> knowledgeSources)
    {
        var extractedKeys = new HashSet<string>(extracted.Keys);
        var changedVars = new List<(string key, FlowVariableOnChange onChange)>();

        foreach (var (key, rawValue) in extracted)
        {
            if (rawValue == null) continue;

            var varDef = flow.Variables.FirstOrDefault(v => v.Key == key);
            if (varDef == null) continue;

            var validationError = Validate(rawValue, varDef, knowledgeSources);
            if (validationError != null)
            {
                ctx.ValidationErrors.Add(new FlowValidationError(key, rawValue, validationError));
                _logger.LogDebug("Validation failed for {Key}='{Value}': {Error}", key, rawValue, validationError);
                continue;
            }

            var oldValue = state.GetVariable(key);
            state.Variables[key] = rawValue;

            if (oldValue != null && !string.Equals(oldValue, rawValue, StringComparison.Ordinal)
                && varDef.OnChange != null)
            {
                changedVars.Add((key, varDef.OnChange));
            }
        }

        if (changedVars.Count == 0) return;

        string? earliestGotoNode = null;
        int earliestOrder = int.MaxValue;

        foreach (var (_, onChange) in changedVars)
        {
            foreach (var resetVar in onChange.ResetVariables)
            {
                if (!extractedKeys.Contains(resetVar))
                    state.Variables.Remove(resetVar);
            }

            foreach (var resetFlag in onChange.ResetFlags)
                state.Flags[resetFlag] = false;

            if (onChange.GotoNode != null)
            {
                var order = flow.GetTopologicalOrder(onChange.GotoNode);
                if (order < earliestOrder)
                {
                    earliestOrder = order;
                    earliestGotoNode = onChange.GotoNode;
                }
            }
        }

        if (earliestGotoNode != null)
        {
            _logger.LogDebug("onChange gotoNode: jumping to {NodeId}", earliestGotoNode);
            state.CurrentNodeId = earliestGotoNode;
            state.IsWaitingForUser = false;
        }
    }

    // ── Prompt builder ─────────────────────────────────────────────────────────

    private string BuildExtractionPrompt(
        List<FlowVariable> variables,
        List<FlowIntentionSchema> activeIntentions,
        FlowExecutionState state,
        string? resolvedExtractionInstructions,
        IEnumerable<KnowledgeSource> knowledgeSources,
        FlowEngineSettings settings)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# EXTRACCIÓN DE INFORMACIÓN — JSON MODE");
        sb.AppendLine("Responde SOLO con JSON válido. Sin texto, markdown ni explicaciones fuera del JSON.");
        sb.AppendLine();

        sb.AppendLine("## Formato de respuesta:");
        sb.AppendLine("{");
        sb.AppendLine("  \"extracted_fields\": [");
        sb.AppendLine("    { \"field_name\": \"string\", \"value\": \"string\", \"confidence\": 0.95 }");
        sb.AppendLine("  ],");

        if (activeIntentions.Count > 0)
        {
            sb.AppendLine("  \"intentions\": {");
            var intentionLines = activeIntentions
                .Select(i => $"    \"{i.Key}\": false")
                .ToList();
            sb.AppendLine(string.Join(",\n", intentionLines));
            sb.AppendLine("  },");
        }

        sb.AppendLine("  \"ambiguities\": [");
        sb.AppendLine("    { \"field_name\": \"string\", \"type\": \"temporal|incomplete|multiple_values|referential\", \"text\": \"expresión original\" }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("## Reglas generales de extracción:");
        sb.AppendLine("- Extrae SOLO del mensaje delimitado con ---MENSAJE A ANALIZAR---.");
        sb.AppendLine("- El historial de conversación es contexto previo para resolver referencias (\"ese\", \"sí\", \"la primera\"), NO fuente de datos a re-extraer.");
        sb.AppendLine("- Si un campo no fue mencionado en el mensaje actual, NO lo incluyas en extracted_fields.");
        sb.AppendLine($"- Solo incluye campos con confidence >= {settings.ExtractionMinConfidence:F1}.");
        if (activeIntentions.Count > 0)
        {
            var intentionKeys = string.Join(", ", activeIntentions.Select(i => $"\"{i.Key}\""));
            sb.AppendLine($"- SEPARACIÓN ESTRICTA: Los nombres ({intentionKeys}) pertenecen EXCLUSIVAMENTE al bloque \"intentions\". NUNCA los incluyas en \"extracted_fields\".");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(resolvedExtractionInstructions))
        {
            sb.AppendLine("## Instrucciones específicas de este flujo:");
            sb.AppendLine(resolvedExtractionInstructions);
            sb.AppendLine();
        }

        if (activeIntentions.Count > 0)
        {
            sb.AppendLine("## Intenciones (detectar del texto, no inventar):");
            foreach (var i in activeIntentions.OrderBy(x => x.Priority))
            {
                sb.Append($"- {i.Key}: {i.Description}");
                if (i.Examples.Count > 0)
                    sb.Append($" — Ej: {string.Join(", ", i.Examples.Take(3).Select(e => $"\"{e}\""))}");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        var catalogSources = knowledgeSources
            .Where(ks => ks.IsActive && ks.Type == KnowledgeSourceType.ServiceCatalog)
            .ToList();

        if (catalogSources.Count > 0)
        {
            var allNames = catalogSources
                .Select(ks => _ksRenderer.RenderNamesOnly(ks))
                .Where(n => !string.IsNullOrWhiteSpace(n));
            var namesList = string.Join(", ", allNames);
            if (!string.IsNullOrWhiteSpace(namesList))
            {
                sb.AppendLine("## Nombres de servicios en catálogo (usar EXACTAMENTE estos nombres al extraer):");
                sb.AppendLine(namesList);
                sb.AppendLine();
            }
        }

        var filled = variables.Where(v => state.GetVariable(v.Key) != null).ToList();
        if (filled.Count > 0)
        {
            sb.AppendLine("## Estado actual (ya recopilado):");
            foreach (var v in filled)
                sb.AppendLine($"- {v.Key}: \"{state.GetVariable(v.Key)}\"");
            sb.AppendLine();
        }

        if (variables.Count > 0)
        {
            sb.AppendLine("## Campos a extraer:");
            sb.AppendLine("| campo | tipo | hint |");
            sb.AppendLine("|-------|------|------|");
            foreach (var v in variables)
            {
                var hint = v.ExtractionHint ?? v.Label;
                sb.AppendLine($"| {v.Key} | {v.DataType} | {hint} |");
            }
        }

        return sb.ToString();
    }

    // ── Response parser ────────────────────────────────────────────────────────

    private FlowExtractionResult ParseExtractionResponse(
        string json,
        List<FlowVariable> variables,
        List<FlowIntentionSchema> activeIntentions,
        double minConfidence)
    {
        var result = new FlowExtractionResult { WasSuccessful = true };
        var validKeys = new HashSet<string>(variables.Select(v => v.Key));

        foreach (var i in activeIntentions)
            result.Intentions[i.Key] = false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("extracted_fields", out var fieldsEl)
                && fieldsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fieldsEl.EnumerateArray())
                {
                    var name = item.TryGetProperty("field_name", out var fn) ? fn.GetString() : null;
                    var value = item.TryGetProperty("value", out var vProp)
                        ? (vProp.ValueKind == JsonValueKind.String ? vProp.GetString() : vProp.GetRawText())
                        : null;
                    var confidence = item.TryGetProperty("confidence", out var cProp)
                        && cProp.TryGetDouble(out var cd) ? cd : 0.6;

                    if (name == null || !validKeys.Contains(name)) continue;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (confidence < minConfidence) continue;

                    result.ExtractedFields[name] = value;
                }
            }

            if (root.TryGetProperty("intentions", out var intentionsEl)
                && intentionsEl.ValueKind == JsonValueKind.Object)
            {
                var activeKeys = new HashSet<string>(activeIntentions.Select(i => i.Key));
                foreach (var prop in intentionsEl.EnumerateObject())
                {
                    if (!activeKeys.Contains(prop.Name)) continue;
                    result.Intentions[prop.Name] = prop.Value.ValueKind == JsonValueKind.True;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response JSON — returning partial result");
        }

        return result;
    }

    // ── Validation ─────────────────────────────────────────────────────────────

    private static string? Validate(
        string value, FlowVariable varDef, IEnumerable<KnowledgeSource> sources)
    {
        var v = varDef.Validation;
        if (v == null) return null;

        if (v.MinLength.HasValue && value.Length < v.MinLength.Value)
            return $"Mínimo {v.MinLength} caracteres";

        if (v.MaxLength.HasValue && value.Length > v.MaxLength.Value)
            return $"Máximo {v.MaxLength} caracteres";

        if (!string.IsNullOrEmpty(v.Pattern))
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, v.Pattern))
                    return "Formato inválido";
            }
            catch { /* ignore bad regex */ }
        }

        if (varDef.DataType == "Number" && double.TryParse(value, out var num))
        {
            if (v.Min.HasValue && num < v.Min.Value)
                return $"Debe ser mayor o igual a {v.Min}";
            if (v.Max.HasValue && num > v.Max.Value)
                return $"Debe ser menor o igual a {v.Max}";
        }

        if (varDef.DataType == "Date" && v.MinDate == "today")
        {
            if (DateOnly.TryParse(value, out var date) && date < DateOnly.FromDateTime(DateTime.Today))
                return "La fecha debe ser hoy o en el futuro";
        }

        return null;
    }
}
