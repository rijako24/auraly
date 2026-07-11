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

    public AgentPromptComposer(IFlowStageDetector flowStageDetector)
    {
        _flowStageDetector = flowStageDetector;
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

        // DetectCurrentStage: llamada unica y cacheada para todo el Compose
        var activeFlow = input.Session is null ? input.Config.Flow : ActiveFlowResolver.Resolve(input.Config, input.Session);
        var currentStage = _flowStageDetector.DetectCurrentStage(activeFlow, input.Session);

        var temporalBlock = input.Temporal.ToPromptBlock();
        if (!string.IsNullOrWhiteSpace(temporalBlock))
            blocks.Add(temporalBlock);

        var openingPolicyBlock = BuildTurnContextBlock(input.History, input.Session);
        if (!string.IsNullOrWhiteSpace(openingPolicyBlock))
            blocks.Add(openingPolicyBlock);

        var stateBlock = BuildStateFactsBlock(input.Config, input.Session, input.LatestPayment);
        if (!string.IsNullOrWhiteSpace(stateBlock))
            blocks.Add(stateBlock);

        var flowBlock = BuildFlowBlock(input.Config, activeFlow, input.Session, currentStage, input.EnabledTools);
        if (!string.IsNullOrWhiteSpace(flowBlock))
            blocks.Add(flowBlock);

        var reentryBlock = BuildReentryBlock(input.Config, activeFlow, input.Session, currentStage);
        if (!string.IsNullOrWhiteSpace(reentryBlock))
            blocks.Add(reentryBlock);

        var globalActionsBlock = BuildGlobalActionsBlock(input.Config, input.EnabledTools, input.Session);
        if (!string.IsNullOrWhiteSpace(globalActionsBlock))
            blocks.Add(globalActionsBlock);


        var actionsBlock = BuildTurnToolsBlock(input, currentStage);
        if (!string.IsNullOrWhiteSpace(actionsBlock))
            blocks.Add(actionsBlock);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    internal static string BuildGlobalActionsBlock(
        AgentConfig config,
        IReadOnlyList<IAgentTool>? effectiveTools = null,
        AgentToolContext? session = null)
    {
        var actions = AgentTurnToolScope.OrderedGlobalActions(config);

        if (actions.Count == 0)
            return string.Empty;

        var effectiveToolNames = effectiveTools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string> { "## REGLAS TRANSVERSALES APLICABLES" };

        foreach (var action in actions)
        {
            var exposeTools = AgentTurnToolScope.ShouldExposeGlobalActionToLlm(action, session);
            if (!exposeTools)
                continue;

            var label = string.IsNullOrWhiteSpace(action.Id) ? "accion" : action.Id.Trim();
            lines.Add($"- {label}: {action.Goal.Trim()}");

            if (action.AllowedActions.Count > 0)
                lines.Add($"  tools: {string.Join(", ", action.AllowedActions)}");

            if (!string.IsNullOrWhiteSpace(action.ConversationGuidance))
                lines.Add($"  orientacion: {action.ConversationGuidance.Trim()}");
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

        // Keys de facts del sistema/sesion que no deben renderizarse al LLM
        var systemKeys = config.FactSchema
            .Where(e => !e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var blocks = new List<string>();
        var paymentForContext = ResolvePaymentForContext(session.ActivePayment, latestPayment);

        // ESTADO ACTUAL: solo facts del schema + extras no-schema
        var factsLines = new List<string> { "## ESTADO ACTUAL" };
        var renderedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Facts declarados en el schema (con label humanizado) - excluye facts de sistema
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

        // 2. Facts extra no declarados en schema - excluye keys de sistema conocidos
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

        var reservationLines = BuildReservationStateLines(config, session);
        if (reservationLines.Count > 0)
            blocks.Add(string.Join(Environment.NewLine, reservationLines));

        // POLITICA DE PAGO
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

    private static List<string> BuildReservationStateLines(AgentConfig config, AgentToolContext session)
    {
        var activeReservations = session.ManageableReservations
            .Where(r => ReservationTemporalFormatter.IsManageableOnBusinessDay(r, session.BusinessToday))
            .ToList();
        if (activeReservations.Count == 0)
            return [];

        var lines = new List<string>
        {
            "## ESTADO RESERVA",
            "- Reservas activas del cliente:"
        };

        foreach (var reservation in activeReservations)
        {
            lines.Add($"- {ReservationTemporalFormatter.FormatLine(reservation, session.BusinessToday)}");
        }

        if (activeReservations.Count > 1 && !string.IsNullOrWhiteSpace(config.ReservationManagement.ManageableReservationGuidance))
        {
            lines.Add($"- guia: {config.ReservationManagement.ManageableReservationGuidance.Trim()}");
        }

        return lines;
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
    /// Compara los facts actuales contra el snapshot guardado al completar etapas anteriores.
    /// Si algun fact relevante cambio, inyecta un bloque ATENCION para que el LLM rehaga las acciones dependientes.
    /// Recibe el currentStage ya detectado para evitar una segunda llamada a DetectCurrentStage.
    /// </summary>
    internal string BuildReentryBlock(
        AgentConfig config,
        AgentFlowDefinition activeFlow,
        AgentToolContext? session,
        AgentFlowStage? currentStage = null)
    {
        if (session?.ConversationState is null || activeFlow.Stages.Count == 0)
            return string.Empty;

        var snapshots = session.ConversationState.StageFactSnapshots;
        if (snapshots.Count == 0)
            return string.Empty;

        var resolvedStage = currentStage ?? _flowStageDetector.DetectCurrentStage(activeFlow, session);
        var currentIdx = resolvedStage is null
            ? activeFlow.Stages.Count
            : activeFlow.Stages.ToList().FindIndex(s => s.Id == resolvedStage.Id);

        var alerts = new List<string>();

        for (var i = 0; i < currentIdx; i++)
        {
            var stage = activeFlow.Stages[i];
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

                alerts.Add($"- {label} cambio de \"{saved ?? "-"}\" a \"{current}\"");
            }
        }

        if (alerts.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "## ATENCION: DATOS MODIFICADOS",
            "El cliente cambio informacion relevante ya procesada. Repite las acciones afectadas antes de continuar:"
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
        AgentFlowDefinition activeFlow,
        AgentToolContext? session,
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> effectiveTools)
    {
        if (activeFlow.Stages.Count == 0 || currentStage is null)
            return string.Empty;

        // Resolver variante activa (si la etapa tiene variantes y hay engagement en facts)
        var variant = FlowStageDetector.GetActiveVariant(currentStage, session);

        var goal = !string.IsNullOrWhiteSpace(variant?.Goal) ? variant.Goal : currentStage.Goal;
        var lines = new List<string>
        {
            "## FLOW ACTUAL",
            $"- flow: {activeFlow.Id}",
            $"- tipo_flow: {(string.IsNullOrWhiteSpace(activeFlow.Type) ? FlowTypes.Primary : activeFlow.Type)}",
            "## ETAPA ACTUAL",
            $"- etapa: {currentStage.Id}",
            $"- objetivo: {goal}"
        };


        AppendConversationalStageLines(config, currentStage, lines, variant);


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
                lines.Add("- criterio_de_avance: la etapa se completa cuando esten presentes estos datos del flujo.");
                lines.Add($"- datos_para_completar_etapa: {string.Join(", ", DescribeFacts(config, missingFacts))}");
                lines.Add("- accion: usa estos datos como proximos datos utiles solo cuando la intencion actual los requiera.");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendConversationalStageLines(
        AgentConfig config,
        AgentFlowStage currentStage,
        List<string> lines,
        AgentFlowStageVariant? variant)
    {

        if (currentStage.Collect.Count > 0)
        {
            lines.Add($"- datos_que_debe_capturar_si_el_cliente_los_menciona: {string.Join(", ", currentStage.Collect)}");
            lines.Add("- regla_collect: si el ultimo mensaje contiene alguno de esos datos y aun no esta en ESTADO ACTUAL, capturalo con una herramienta antes de responder; collect no bloquea el avance de etapa.");
        }

        var guidance = !string.IsNullOrWhiteSpace(variant?.ConversationGuidance)
            ? variant!.ConversationGuidance
            : currentStage.ConversationGuidance;
        if (!string.IsNullOrWhiteSpace(guidance))
            lines.Add($"- guia_conversacional: {guidance.Trim()}");

        if (!string.IsNullOrWhiteSpace(currentStage.OnSuccess))
            lines.Add($"- si_resulta_bien: {currentStage.OnSuccess.Trim()}");

        if (!string.IsNullOrWhiteSpace(currentStage.OnProblem))
            lines.Add($"- si_hay_problema: {currentStage.OnProblem.Trim()}");
    }

    private string BuildTurnToolsBlock(PromptCompositionInput input, AgentFlowStage? currentStage)
    {
        if (input.EnabledTools.Count == 0)
            return string.Empty;

        var tools = input.Session is null
            ? input.EnabledTools
            : AgentTurnToolScope.Resolve(input.Config, input.Session, input.EnabledTools, currentStage);

        if (tools.Count == 0)
            return string.Empty;

        var names = tools
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            "## HERRAMIENTAS DE ESTE TURNO",
            string.Join(", ", names),
            "- Si este turno ya incluye un resultado de herramienta, usa esa salida como fuente vigente para responder; no repitas la misma herramienta salvo que falte informacion necesaria."
        };


        return string.Join(Environment.NewLine, lines);
    }

    private static PaymentTransaction? ResolvePaymentForContext(
        PaymentTransaction? activePayment,
        PaymentTransaction? latestPayment)
    {
        return ResolveActionablePayment(activePayment)
            ?? ResolveActionablePayment(latestPayment);
    }

    private static PaymentTransaction? ResolveActionablePayment(PaymentTransaction? payment)
    {
        if (payment is null)
            return null;

        if (payment.Status == PaymentTransactionStatus.Created)
            return payment;

        if (payment.Status == PaymentTransactionStatus.Confirmed
            && (!payment.ReservationId.HasValue || payment.RequiresRescheduling))
        {
            return payment;
        }

        return null;
    }

    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase) ||
        sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);
}
