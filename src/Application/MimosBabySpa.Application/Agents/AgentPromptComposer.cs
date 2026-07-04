using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Compone el system prompt runtime a partir de Persona, Flow, Facts, Guards y contexto de turno.
/// </summary>
public sealed class AgentPromptComposer : IPromptComposer
{
    private readonly IFlowStageDetector _flowStageDetector;
    private readonly IGuardEvaluator _guardEvaluator;

    public AgentPromptComposer(IFlowStageDetector flowStageDetector, IGuardEvaluator guardEvaluator)
    {
        _flowStageDetector = flowStageDetector;
        _guardEvaluator = guardEvaluator;
    }

    public string Compose(PromptCompositionInput input)
    {
        var operatingHoursBlock = BuildOperatingHoursBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(operatingHoursBlock))
            return operatingHoursBlock;

        var blocks = new List<string>();

        var basePrompt = input.Config.BasePrompt;
        if (!string.IsNullOrWhiteSpace(basePrompt))
            blocks.Add(basePrompt.Trim());

        // DetectCurrentStage: llamada única y cacheada para todo el Compose
        var currentStage = _flowStageDetector.DetectCurrentStage(input.Config.Flow, input.Session);

        var eagerBlock = BuildEagerCaptureBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(eagerBlock))
            blocks.Add(eagerBlock);

        var temporalBlock = input.Temporal.ToPromptBlock();
        if (!string.IsNullOrWhiteSpace(temporalBlock))
            blocks.Add(temporalBlock);

        var openingPolicyBlock = BuildTurnContextBlock(input.History, input.Session);
        if (!string.IsNullOrWhiteSpace(openingPolicyBlock))
            blocks.Add(openingPolicyBlock);

        var stateBlock = BuildStateFactsBlock(input.Config, input.Session, input.LatestPayment);
        if (!string.IsNullOrWhiteSpace(stateBlock))
            blocks.Add(stateBlock);

        var flowBlock = BuildFlowBlock(input.Config, input.Session, currentStage, input.EnabledTools);
        if (!string.IsNullOrWhiteSpace(flowBlock))
            blocks.Add(flowBlock);

        var reentryBlock = BuildReentryBlock(input.Config, input.Session, currentStage);
        if (!string.IsNullOrWhiteSpace(reentryBlock))
            blocks.Add(reentryBlock);

        var globalActionsBlock = BuildGlobalActionsBlock(input.Config, input.EnabledTools);
        if (!string.IsNullOrWhiteSpace(globalActionsBlock))
            blocks.Add(globalActionsBlock);


        var actionsBlock = BuildActionsBlock(input);
        if (!string.IsNullOrWhiteSpace(actionsBlock))
            blocks.Add(actionsBlock);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    internal static string BuildGlobalActionsBlock(AgentConfig config, IReadOnlyList<IAgentTool>? effectiveTools = null)
    {
        if (config.GlobalActions.Count == 0)
            return string.Empty;

        var effectiveToolNames = effectiveTools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "## ACCIONES TRANSVERSALES",
            "- Estas acciones pueden usarse cuando apliquen, aunque la etapa actual tenga otro objetivo."
        };

        foreach (var action in config.GlobalActions
                     .OrderByDescending(a => a.Priority)
                     .ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrWhiteSpace(action.Id) ? "accion" : action.Id.Trim();
            lines.Add($"- {label}: {action.Goal.Trim()}");
            var allowedTools = effectiveToolNames is null
                ? action.AllowedTools
                : action.AllowedTools.Where(effectiveToolNames.Contains).ToList();
            if (allowedTools.Count > 0)
                lines.Add($"  herramientas: {string.Join(", ", allowedTools)}");
            if (!string.IsNullOrWhiteSpace(action.Hint))
                lines.Add($"  orientacion: {action.Hint.Trim()}");
        }

        return string.Join(Environment.NewLine, lines);
    }
    internal static string BuildOperatingHoursBlock(AgentConfig config, AgentToolContext? session)
    {
        var policy = session?.OperatingHours;
        if (policy is null || !policy.IsEnforced || !policy.IsOutsideOperatingHours)
            return string.Empty;

        var blocks = new List<string>();
        var basePrompt = config.BasePrompt;
        if (!string.IsNullOrWhiteSpace(basePrompt))
            blocks.Add(basePrompt.Trim());

        var lines = new List<string>
        {
            "## DISPONIBILIDAD ACTUAL",
            "- El negocio esta fuera de horario laboral en este momento.",
            "- Responde como el agente de este negocio, con la identidad y tono definidos arriba.",
            "- Adapta la respuesta al ultimo mensaje del cliente; no repitas literalmente la misma plantilla en todos los turnos.",
            "- No inicies, confirmes ni avances gestiones operativas del negocio mientras este fuera de horario.",
            "- Si el cliente solo saluda, saluda y agradece el contacto de forma natural; luego explica brevemente que ahora no estamos disponibles.",
            "- Si el cliente pide avanzar alguna gestion, explica brevemente que no podemos gestionarla fuera del horario laboral.",
            "- Si el cliente pregunta por que, responde brevemente que estamos fuera del horario laboral.",
            "- No solicites datos, no prometas ejecutar acciones y no hables de un tipo de gestion que el cliente no haya mencionado.",
            "- Responde de forma breve y cerrada: no termines con preguntas ni ofrezcas continuar flujos, catalogos o recomendaciones fuera de horario."
        };

        if (!string.IsNullOrWhiteSpace(policy.NextOperatingWindowText))
        {
            lines.Insert(1, $"- proximo_horario_habil: {policy.NextOperatingWindowText}.");
            lines.Insert(2, "- Cuando menciones el proximo horario, usa proximo_horario_habil. Si empieza por hoy, no agregues fecha ni dia.");
        }

        blocks.Add(string.Join(Environment.NewLine, lines));
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }
    internal static string BuildTurnContextBlock(IEnumerable<Message> history, AgentToolContext? session)
    {
        var isFirstVisibleResponse = !history.Any(m => IsBotSender(m.Sender));
        var isNewBusinessDay = session?.BusinessDayRollover == true;
        if (!isFirstVisibleResponse && !isNewBusinessDay)
            return string.Empty;

        var reason = isFirstVisibleResponse
            ? "primera respuesta visible"
            : "nuevo dia operativo";

        var lines = new List<string>
        {
            "## CONTEXTO DEL TURNO",
            "- apertura_requerida: true",
            $"- motivo_apertura: {reason}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    internal static string BuildStateFactsBlock(
        AgentConfig config,
        AgentToolContext? session,
        PaymentTransaction? latestPayment = null)
    {
        if (session is null)
            return string.Empty;

        // Keys de facts del sistema/sesión que no deben renderizarse al LLM
        var systemKeys = config.FactSchema
            .Where(e => !e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var blocks = new List<string>();
        var paymentForContext = ResolvePaymentForContext(session?.ActivePayment, latestPayment);

        // ── ESTADO ACTUAL: solo facts del schema + extras no-schema ──────────
        if (session is not null)
        {
            var factsLines = new List<string> { "## ESTADO ACTUAL" };
            var renderedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Facts declarados en el schema (con label humanizado) — excluye facts de sistema
            foreach (var entry in config.FactSchema.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
                    continue;

                session.Facts.TryGetValue(entry.Key, out var raw);
                var value = !string.IsNullOrWhiteSpace(raw) ? raw : null;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    var label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Key : entry.Label;
                    factsLines.Add($"- {label}: {value}");
                    renderedKeys.Add(entry.Key);
                }
            }

            // 2. Facts extra no declarados en schema — excluye keys de sistema conocidos
            foreach (var (key, value) in session.Facts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (renderedKeys.Contains(key)
                    || string.IsNullOrWhiteSpace(value)
                    || systemKeys.Contains(key)
                    || key.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                factsLines.Add($"- {key}: {value}");
            }

            if (factsLines.Count > 1)
            {
                factsLines.Add("- No vuelvas a llamar set_fact para un dato ya listado arriba con el mismo valor.");
                blocks.Add(string.Join(Environment.NewLine, factsLines));
            }
        }

        // ── POLÍTICA DE PAGO ──────────────────────────────────────────────────
        var policyLines = new List<string>();
        var paymentLine = TurnContextPaymentFormatter.FormatPaymentLine(paymentForContext);
        if (!string.IsNullOrWhiteSpace(paymentLine))
            policyLines.Add(paymentLine);

        if (policyLines.Count > 0)
            blocks.Add(string.Join(Environment.NewLine, policyLines));

        return blocks.Count == 0
            ? string.Empty
            : string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    private static IEnumerable<string> DescribeFacts(AgentConfig config, IReadOnlyList<string> factKeys)
    {
        foreach (var factKey in factKeys)
        {
            var entry = config.FactSchema.FirstOrDefault(e =>
                e.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase));
            var label = entry is not null && !string.IsNullOrWhiteSpace(entry.Label)
                ? entry.Label
                : factKey;

            yield return $"{label} ({factKey})";
        }
    }

    /// <summary>
    /// Emite una instrucción permanente con los facts que deben capturarse de inmediato
    /// (captureMode=eager) aunque el flujo aún no haya llegado a la etapa que los solicita.
    /// Solo lista los facts de usuario que todavía no tienen valor.
    /// </summary>
    internal static string BuildEagerCaptureBlock(AgentConfig config, AgentToolContext? session)
    {
        var eagerMissing = config.FactSchema
            .Where(e => e.CaptureMode.Equals("eager", StringComparison.OrdinalIgnoreCase)
                     && e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Where(e =>
            {
                if (session is null) return true;
                return !session.Facts.TryGetValue(e.Key, out var raw) || string.IsNullOrWhiteSpace(raw);
            })
            .ToList();

        if (eagerMissing.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "## CAPTURA INMEDIATA",
            "Si el cliente menciona alguno de los siguientes datos antes de que los solicites, persístalo de inmediato con set_fact:",
            "- Guarda únicamente datos expresados o confirmados por el cliente.",
            "- Mantén objetivos internos y marcadores de estado fuera de facts de usuario."
        };

        foreach (var entry in eagerMissing)
        {
            var label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Key : entry.Label;
            lines.Add($"- {label} ({entry.Key})");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Compara los facts actuales contra el snapshot guardado al completar etapas anteriores.
    /// Si algún fact relevante cambió, inyecta un bloque ATENCIÓN para que el LLM rehaga las acciones dependientes.
    /// Recibe el currentStage ya detectado para evitar una segunda llamada a DetectCurrentStage.
    /// </summary>
    internal string BuildReentryBlock(
        AgentConfig config,
        AgentToolContext? session,
        AgentFlowStage? currentStage = null)
    {
        if (session is null || config.Flow.Stages.Count == 0)
            return string.Empty;

        var snapshots = session.ConversationState.StageFactSnapshots;
        if (snapshots.Count == 0)
            return string.Empty;

        var resolvedStage = currentStage ?? _flowStageDetector.DetectCurrentStage(config.Flow, session);
        var currentIdx = resolvedStage is null
            ? config.Flow.Stages.Count
            : config.Flow.Stages.ToList().FindIndex(s => s.Id == resolvedStage.Id);

        var alerts = new List<string>();

        for (var i = 0; i < currentIdx; i++)
        {
            var stage = config.Flow.Stages[i];
            if (stage.ReentryOnFactChanged.Count == 0)
                continue;

            if (!snapshots.TryGetValue(stage.Id, out var snapshot))
                continue;

            foreach (var factKey in stage.ReentryOnFactChanged)
            {
                session.Facts.TryGetValue(factKey, out var current);
                snapshot.TryGetValue(factKey, out var saved);

                if (string.IsNullOrWhiteSpace(current) || current == saved)
                    continue;

                var entry = config.FactSchema.FirstOrDefault(e =>
                    e.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase));
                var label = entry is not null && !string.IsNullOrWhiteSpace(entry.Label)
                    ? entry.Label
                    : factKey;

                alerts.Add($"- {label} cambió de \"{saved ?? "—"}\" a \"{current}\"");
            }
        }

        if (alerts.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "## ATENCIÓN: DATOS MODIFICADOS",
            "El cliente cambió información relevante ya procesada. Repite las acciones afectadas antes de continuar:"
        };
        lines.AddRange(alerts);

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Construye el bloque de la etapa actual, aplicando la variante correspondiente
    /// al engagement del turno si la etapa tiene variantes declaradas.
    /// </summary>
    private string BuildFlowBlock(
        AgentConfig config,
        AgentToolContext? session,
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> effectiveTools)
    {
        if (config.Flow.Stages.Count == 0 || currentStage is null)
            return string.Empty;

        // Resolver variante activa (si la etapa tiene variantes y hay engagement en facts)
        var variant = FlowStageDetector.GetActiveVariant(currentStage, session);

        var goal = !string.IsNullOrWhiteSpace(variant?.Goal) ? variant.Goal : currentStage.Goal;
        var lines = new List<string>
        {
            "## ETAPA ACTUAL",
            $"- etapa: {currentStage.Id}",
            $"- objetivo: {goal}"
        };

        if (currentStage.AllowedTools.Count > 0)
        {
            var effectiveToolNames = effectiveTools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowedTools = currentStage.AllowedTools.Where(effectiveToolNames.Contains).ToList();
            if (allowedTools.Count > 0)
                lines.Add($"- acciones_permitidas: {string.Join(", ", allowedTools)}");
        }

        var stageHint = !string.IsNullOrWhiteSpace(variant?.Hint) ? variant!.Hint : currentStage.Hint;
        if (!string.IsNullOrWhiteSpace(stageHint))
        {
            lines.Add(string.Empty);
            lines.Add(variant?.Hint is not null
                ? "Orientación para este engagement:"
                : "Qué hacer ahora:");
            lines.Add($"- {stageHint.Trim()}");
        }

        if (session is not null && currentStage.AdvanceWhenFacts.Count > 0)
        {
            var userFactKeys = config.FactSchema
                .Where(f => f.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingFacts = currentStage.AdvanceWhenFacts
                .Where(userFactKeys.Contains)
                .Where(f => !session.Facts.TryGetValue(f, out var v) || string.IsNullOrWhiteSpace(v))
                .ToList();

            if (missingFacts.Count > 0)
            {
                lines.Add("- criterio_de_avance: la etapa se completa cuando estén presentes estos datos del flujo.");
                lines.Add($"- datos_para_completar_etapa: {string.Join(", ", DescribeFacts(config, missingFacts))}");
                lines.Add("- acción: usa estos datos como próximos datos útiles solo cuando la intención actual los requiera.");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildActionsBlock(PromptCompositionInput input)
    {
        if (input.EnabledTools.Count == 0)
            return string.Empty;

        using var emptyArgs = System.Text.Json.JsonDocument.Parse("{}");
        var available = new List<string>();
        var blocked = new List<string>();

        foreach (var tool in input.EnabledTools)
        {
            var session = input.Session;
            if (session is null)
            {
                available.Add(tool.Name);
                continue;
            }

            var eval = _guardEvaluator.EvaluateTool(tool, input.Config, session, emptyArgs.RootElement);
            if (eval.IsAvailable)
                available.Add(tool.Name);
            else
                blocked.Add($"- {tool.Name}: {eval.BlockReason}");
        }

        var lines = new List<string>();
        if (available.Count > 0)
        {
            lines.Add("## ACCIONES DISPONIBLES");
            lines.Add(string.Join(", ", available));
        }

        if (blocked.Count > 0)
        {
            lines.Add("## ACCIONES BLOQUEADAS");
            lines.AddRange(blocked);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static PaymentTransaction? ResolvePaymentForContext(
        PaymentTransaction? activePayment,
        PaymentTransaction? latestPayment)
    {
        if (activePayment?.Status == PaymentTransactionStatus.Confirmed)
            return activePayment;

        if (latestPayment?.Status == PaymentTransactionStatus.Confirmed)
            return latestPayment;

        if (activePayment is not null)
            return activePayment;

        return latestPayment;
    }

    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase) ||
        sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);
}
