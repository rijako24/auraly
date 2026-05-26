using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// LLM del flujo: respuesta en JSON + function calling. Los facts de usuario se persisten solo vía <c>set_fact</c>.
/// </summary>
public sealed class FlowLlm : IFlowLlm
{
    private readonly IChatClient _chat;
    private readonly ILogger<FlowLlm> _logger;

    public FlowLlm(IChatClient chat, ILogger<FlowLlm> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<FlowTurnResult> RunAsync(FlowLlmRequest request, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(request);
        var messages = new List<ChatMessage> { ChatMessage.System(systemPrompt) };

        foreach (var msg in request.History.TakeLast(6))
        {
            var role = msg.Sender.Equals("bot", StringComparison.OrdinalIgnoreCase)
                || msg.Sender.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? "assistant" : "user";

            if (role == "user")
                messages.Add(ChatMessage.User(msg.MessageText));
            else
                messages.Add(ChatMessage.Assistant(msg.MessageText));
        }

        foreach (var extra in request.ExtraMessages)
            messages.Add(extra);

        if (!string.IsNullOrWhiteSpace(request.UserMessage))
            messages.Add(ChatMessage.User(request.UserMessage));

        var result = await _chat.CompleteAsync(
            messages,
            tools: request.AvailableTools.Count > 0 ? request.AvailableTools : null,
            options: new ChatCompletionOptions
            {
                Temperature = request.Config.Temperature,
                MaxTokens = request.Config.OperationalLimits.MaxResponseTokens,
                ForceTextResponse = false,
                ForceJsonResponse = true,
                DeploymentNameOverride = request.Config.Model
            },
            cancellationToken: ct);

        if (!result.Success)
        {
            _logger.LogWarning("FlowLlm: LLM failed for stage {Stage}: {Error}",
                request.Stage?.Id ?? "<none>", result.ErrorMessage);
            return FlowTurnResult.Fallback("Disculpa, tuve un problema. ¿Podrías repetir tu mensaje?");
        }

        _logger.LogInformation("FlowLlm: stage={Stage} tokens={T}", request.Stage?.Id ?? "<none>",
            result.PromptTokens + result.CompletionTokens);

        if (result.FinishReason == ChatCompletionFinishReason.ToolCalls)
        {
            return new FlowTurnResult
            {
                Intent = "Continue",
                Reply = string.Empty,
                ToolCalls = result.ToolCalls,
                Tokens = result.PromptTokens + result.CompletionTokens
            };
        }

        if (string.IsNullOrWhiteSpace(result.Content))
            return FlowTurnResult.Fallback("Disculpa, tuve un problema. ¿Podrías repetir tu mensaje?");

        _logger.LogDebug("FlowLlm raw content for stage {Stage}: {Content}",
            request.Stage?.Id ?? "<none>", result.Content);
        return ParseResponse(result.Content, result.PromptTokens + result.CompletionTokens);
    }

    private static string BuildSystemPrompt(FlowLlmRequest req)
    {
        var config = req.Config;
        var sb = new StringBuilder();

        sb.AppendLine(config.BasePrompt);
        sb.AppendLine();

        sb.AppendLine("## MOTOR DE FLUJO");
        sb.AppendLine($"Stage actual: **{req.Stage?.Id ?? "<none>"}**");
        if (!string.IsNullOrWhiteSpace(req.Stage?.Ask))
            sb.AppendLine($"Objetivo: {req.Stage.Ask}");
        sb.AppendLine();

        var hasSetFact = req.AvailableTools.Any(t =>
            t.Name.Equals("set_fact", StringComparison.OrdinalIgnoreCase));

        var collectKeys = FactSchemaPrompt.ResolveCollectKeys(config.FactSchema, req.StageCollects);
        var collectEntries = FactSchemaPrompt.EntriesForKeys(config.FactSchema, collectKeys);
        var missingCollect = FactSchemaPrompt.MissingUserFactKeys(
            config.FactSchema, collectKeys, req.KnownFacts);
        if (hasSetFact && collectEntries.Count > 0)
        {
            sb.AppendLine("### DATOS DE ESTE STAGE (persistencia con set_fact)");
            sb.AppendLine(
                "Cuando el cliente proporcione un dato de esta lista, llama **set_fact** (una llamada por clave) " +
                "con valor estructurado según el tipo. Usa la clave canónica exacta.");
            foreach (var f in collectEntries)
                sb.AppendLine($"- `{f.Key}` ({f.Type}): {f.Label}");
            if (missingCollect.Count > 0)
                sb.AppendLine($"Faltan por capturar: {string.Join(", ", missingCollect)}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(req.LookupOmittedHint))
        {
            sb.AppendLine("### LOOKUP OMITIDO (motor)");
            sb.AppendLine(req.LookupOmittedHint.Trim());
            sb.AppendLine("No intentes consultar disponibilidad hasta tener esos datos. Pídelos al cliente.");
            sb.AppendLine();
        }

        var knownPairs = req.KnownFacts
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToList();

        if (knownPairs.Count > 0)
        {
            sb.AppendLine("### FACTS YA GUARDADOS (no repetir set_fact salvo corrección explícita)");
            foreach (var (k, v) in knownPairs)
                sb.AppendLine($"- {k}: {v}");
            sb.AppendLine();
        }

        if (FlowStageLookupPresentation.IsLlmCurate(req.Stage))
        {
            var catalogRef = CatalogReferenceFormatter.FormatServicesForPrompt(req.LookupResult);
            if (!string.IsNullOrWhiteSpace(catalogRef))
            {
                sb.AppendLine("### CATÁLOGO (referencia interna — no copies todo al cliente)");
                sb.AppendLine(catalogRef);
                sb.AppendLine();

                sb.AppendLine("### PRESENTACIÓN DE PLANES");
                if (knownPairs.Count > 0)
                {
                    sb.AppendLine("Facts conocidos del cliente (usa solo para criterios explícitos en la descripción de cada plan):");
                    foreach (var (k, v) in knownPairs)
                        sb.AppendLine($"- {k}: {v}");
                    sb.AppendLine();
                }

                sb.AppendLine(
                    "Lista TODOS los planes recibidos en el catálogo. " +
                    "Solo omite uno si su descripción dice EXPLÍCITAMENTE un criterio que excluye al cliente " +
                    "(ej. un rango numérico que no aplica). " +
                    "Si la descripción no menciona el criterio del fact, INCLÚYELO. Ante duda: incluir.");
                sb.AppendLine(
                    "No agregues planes que no estén en el catálogo. No inventes descripciones ni precios. " +
                    "Formato WhatsApp: claro, con emojis moderados. Cierra preguntando cuál plan le interesa.");
                sb.AppendLine();
            }
        }
        else if (!string.IsNullOrWhiteSpace(req.RenderedTemplate))
        {
            sb.AppendLine("### BLOQUE A INCLUIR VERBATIM");
            sb.AppendLine("El siguiente bloque DEBE aparecer EXACTAMENTE ASÍ al inicio de tu `reply`:");
            sb.AppendLine("```");
            sb.AppendLine(req.RenderedTemplate.Trim());
            sb.AppendLine("```");
            sb.AppendLine("No parafrasees ni omitas nada del bloque. Puedes agregar texto DESPUÉS si es útil.");
            sb.AppendLine();
        }

        sb.AppendLine($"Fecha de hoy: {DateTime.UtcNow:yyyy-MM-dd} (referencia; usa el contexto del negocio si disponible)");
        sb.AppendLine();

        if (req.AvailableTools.Count > 0)
        {
            sb.AppendLine("### HERRAMIENTAS DISPONIBLES");
            foreach (var tool in req.AvailableTools)
                sb.AppendLine($"- {tool.Name}: {tool.Description}");
            sb.AppendLine();
            if (hasSetFact)
            {
                sb.AppendLine(
                    "Si el cliente proporcionó datos nuevos en su mensaje: (1) set_fact por cada uno, " +
                    "(2) otras tools si aplican. Si no proporcionó datos, ve directo al JSON final.");
            }
            sb.AppendLine(
                "Para usar una tool, devuelve la llamada a función. " +
                "Cuando no haga falta más ninguna tool, responde con el JSON final descrito abajo.");
            sb.AppendLine();
        }

        sb.AppendLine("## RESPUESTA FINAL (cuando no haya más tool calls)");
        sb.AppendLine("Responde ÚNICAMENTE con este JSON (sin markdown):");
        sb.AppendLine("""
            {
              "intent": "Continue | Confirm | Deny | OffTopic | Escalate",
              "reply": "tu respuesta al cliente"
            }
            """);
        sb.AppendLine("No incluyas facts en el JSON: la persistencia es exclusivamente con set_fact.");
        sb.AppendLine();
        sb.AppendLine("Guía de intents:");
        sb.AppendLine("- Continue: interacción normal");
        sb.AppendLine("- Confirm: confirma explícitamente (sí, confirmo, de acuerdo)");
        sb.AppendLine("- Deny: rechaza o cancela");
        sb.AppendLine("- OffTopic: fuera del flujo; breve y vuelve al tema");
        sb.AppendLine("- Escalate: pide humano o mucha frustración");

        return sb.ToString();
    }

    internal static FlowTurnResult ParseResponse(string content, int tokens)
    {
        try
        {
            var json = ExtractJson(content);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intent = root.TryGetProperty("intent", out var intentEl)
                ? intentEl.GetString() ?? "Continue"
                : "Continue";

            var reply = root.TryGetProperty("reply", out var replyEl)
                ? replyEl.GetString() ?? string.Empty
                : string.Empty;

            return new FlowTurnResult
            {
                Intent = NormalizeIntent(intent),
                Reply = reply,
                Tokens = tokens
            };
        }
        catch
        {
            return FlowTurnResult.Fallback(content.Trim());
        }
    }

    private static string NormalizeIntent(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "confirm" or "confirmar" or "yes" or "si" => "Confirm",
            "deny" or "negar" or "no" => "Deny",
            "offtopic" or "off_topic" or "off-topic" => "OffTopic",
            "escalate" or "escalar" or "frustration" => "Escalate",
            _ => "Continue"
        };

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}
