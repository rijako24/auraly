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
        var blocks = new List<string>();

        var basePrompt = input.Config.BasePrompt;
        if (!string.IsNullOrWhiteSpace(basePrompt))
            blocks.Add(basePrompt.Trim());

        var memoryBlock = BuildCustomerMemoryBlock(input.Session);
        if (!string.IsNullOrWhiteSpace(memoryBlock))
            blocks.Add(memoryBlock);

        // DetectCurrentStage: llamada única y cacheada para todo el Compose
        var currentStage = _flowStageDetector.DetectCurrentStage(input.Config.Flow, input.Session);

        var eagerBlock = BuildEagerCaptureBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(eagerBlock))
            blocks.Add(eagerBlock);

        var temporalBlock = input.Temporal.ToPromptBlock();
        if (!string.IsNullOrWhiteSpace(temporalBlock))
            blocks.Add(temporalBlock);

        var stateBlock = BuildStateFactsBlock(input.Config, input.Session, input.LatestPayment);
        if (!string.IsNullOrWhiteSpace(stateBlock))
            blocks.Add(stateBlock);

        var flowBlock = BuildFlowBlock(input.Config, input.Session, currentStage);
        if (!string.IsNullOrWhiteSpace(flowBlock))
            blocks.Add(flowBlock);

        var reentryBlock = BuildReentryBlock(input.Config, input.Session, currentStage);
        if (!string.IsNullOrWhiteSpace(reentryBlock))
            blocks.Add(reentryBlock);

        var actionsBlock = BuildActionsBlock(input);
        if (!string.IsNullOrWhiteSpace(actionsBlock))
            blocks.Add(actionsBlock);

        var triggersBlock = BuildSemanticTriggersBlock(input.EnabledTools, input.Config);
        if (!string.IsNullOrWhiteSpace(triggersBlock))
            blocks.Add(triggersBlock);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    internal static string BuildCustomerMemoryBlock(AgentToolContext? session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.CustomerMemorySummary))
            return string.Empty;

        return string.Join(Environment.NewLine, new[]
        {
            "## MEMORIA DEL CLIENTE (visitas anteriores)",
            session.CustomerMemorySummary.Trim(),
            "- Úsalo solo como contexto; NO repitas reservas pasadas como si fueran nuevas."
        });
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
                    || systemKeys.Contains(key))
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

            // ── ESTADO RESERVA ─────────────────────────────────────────────────
            var reservationBlock = BuildReservationContextBlock(session);
            if (!string.IsNullOrWhiteSpace(reservationBlock))
                blocks.Add(reservationBlock);

            // ── ESTADO PAGO: pago confirmado sin slot ─────────────────────────
            if (paymentForContext?.RequiresRescheduling == true
                && paymentForContext.Status == PaymentTransactionStatus.Confirmed
                && !paymentForContext.ReservationId.HasValue)
            {
                var paySpecial = new List<string>
                {
                    "## ESTADO PAGO",
                    "- pago_confirmado_sin_slot: true",
                    $"- payment_transaction_id: {paymentForContext.PaymentTransactionId}",
                    "- accion_requerida: cuando el cliente confirme nuevo horario, llama assign_paid_slot"
                };
                blocks.Add(string.Join(Environment.NewLine, paySpecial));
            }

            var pendingCheckoutBlock = BuildPendingCheckoutBlock(paymentForContext);
            if (!string.IsNullOrWhiteSpace(pendingCheckoutBlock))
                blocks.Add(pendingCheckoutBlock);
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

    internal static string BuildReservationContextBlock(AgentToolContext session)
    {
        return session.ManageableReservations.Count switch
        {
            0 => string.Empty,
            1 => BuildSingleReservationBlock(session.ManageableReservations[0]),
            _ => BuildMultipleReservationsBlock(session.ManageableReservations)
        };
    }

    private static string BuildSingleReservationBlock(Reservation r)
    {
        var lines = new List<string> { "## ESTADO RESERVA", $"- estado: {r.Status}" };

        var serviceName = r.Service?.ServiceName ?? r.GetServiceName();
        if (!string.IsNullOrWhiteSpace(serviceName))
            lines.Add($"- servicio: {serviceName}");

        if (r.ReservationDateTime is not null)
        {
            lines.Add($"- fecha_confirmada: {DateOnly.FromDateTime(r.ReservationDateTime.Value):yyyy-MM-dd}");
            lines.Add($"- hora_confirmada: {TimeOnly.FromDateTime(r.ReservationDateTime.Value):HH:mm}");
        }

        if (r.ReservationId != Guid.Empty)
            lines.Add($"- id_reserva: {r.ReservationId}");

        lines.Add("- gestion: reagendar o cancelar usando las tools; no pidas UUID al cliente.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMultipleReservationsBlock(IReadOnlyList<Reservation> reservations)
    {
        var lines = new List<string>
        {
            "## RESERVAS DEL CLIENTE",
            "- varias_citas: true",
            "- accion: pregunta cuál cita (fecha y servicio); nunca pidas UUID al cliente."
        };

        var index = 1;
        foreach (var r in reservations)
        {
            lines.Add($"- cita_{index}: {CustomerReservationResolver.FormatReservationLine(r)}");
            index++;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildPendingCheckoutBlock(PaymentTransaction? payment)
    {
        if (payment?.Status != PaymentTransactionStatus.Created)
            return string.Empty;

        var lines = new List<string>
        {
            "## CHECKOUT PENDIENTE",
            "- hay_link_pendiente: true",
            $"- checkout_kind: {payment.CheckoutKind}",
            $"- monto_pendiente: {payment.Currency} ${(payment.AmountInCents / 100m).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}",
            "- regla: el cliente puede pedir informacion normal sin modificar la solicitud.",
            "- si el cliente cambia servicio, complementos, fecha, hora u horario fijo: actualiza primero los facts con set_fact y luego reconstruye el resumen/link con prepare_checkout.",
            "- si el cliente elige una opcion concreta que acabas de listar, esa eleccion autoriza el siguiente paso del flujo: usa el nombre exacto de la opcion como service, llama prepare_checkout y entrega el resumen/link resultante.",
            "- si el nuevo servicio es de inscripcion a horario fijo: no pidas fecha ni hora flexible; usa el horario fijo del servicio y registra fixed_schedule_label y fulfillment_ready=enrollment.",
            "- si el cliente quiere cancelar o desistir de esta solicitud: llama reset_flow_context con checkout_action=abandon.",
            "- no llames create_reservation mientras el pago siga pendiente."
        };

        if (!string.IsNullOrWhiteSpace(payment.LinkUrl))
            lines.Add($"- link_actual: {payment.LinkUrl}");

        return string.Join(Environment.NewLine, lines);
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
            "Si el cliente menciona alguno de los siguientes datos antes de que los solicites, persístalo de inmediato con set_fact:"
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
        AgentFlowStage? currentStage)
    {
        if (config.Flow.Stages.Count == 0 || currentStage is null)
            return string.Empty;

        // Resolver variante activa (si la etapa tiene variantes y hay engagement en facts)
        var variant = FlowStageDetector.GetActiveVariant(currentStage, session);

        var goal = !string.IsNullOrWhiteSpace(variant?.Goal) ? variant.Goal : currentStage.Goal;
        var constraints = variant?.Constraints ?? currentStage.Constraints;

        var lines = new List<string>
        {
            "## ETAPA ACTUAL",
            $"- etapa: {currentStage.Id}",
            $"- objetivo: {goal}"
        };

        if (currentStage.AllowedTools.Count > 0)
            lines.Add($"- acciones_permitidas: {string.Join(", ", currentStage.AllowedTools)}");

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
            var missingFacts = currentStage.AdvanceWhenFacts
                .Where(f => !session.Facts.TryGetValue(f, out var v) || string.IsNullOrWhiteSpace(v))
                .ToList();

            if (missingFacts.Count > 0)
            {
                lines.Add($"- facts_pendientes: {string.Join(", ", missingFacts)}");
                lines.Add($"- datos_pendientes_para_avanzar: {string.Join(", ", DescribeFacts(config, missingFacts))}");
                lines.Add(
                    "- Mientras existan facts_pendientes, NO preguntes por acciones de etapas futuras ni intentes avanzar. " +
                    "Si debes responder al cliente, pide solo el primer dato pendiente de esta lista.");
                lines.Add("- Regístralos con set_fact en cuanto el cliente los confirme en este turno.");
            }

            lines.Add(
                "- Concéntrate únicamente en el objetivo de esta etapa y ciérrala con la pregunta o acción que le corresponde. "
                + "Cuando los datos necesarios queden registrados, el sistema te llevará automáticamente al siguiente paso.");
        }

        // Traducir restricciones declarativas a instrucciones para el LLM
        if (constraints is not null)
        {
            var constraintLines = new List<string>();

            if (constraints.MaxQuestions.HasValue)
            {
                constraintLines.Add(constraints.MaxQuestions.Value == 0
                    ? "- NO hagas preguntas en este turno; solo responde o saluda."
                    : $"- Haz como máximo {constraints.MaxQuestions.Value} pregunta(s) en este turno.");
            }

            if (!string.IsNullOrWhiteSpace(constraints.PresentationMode))
            {
                constraintLines.Add(constraints.PresentationMode == "soft_offer"
                    ? "- Presenta las opciones de forma amable y no presiones. Termina con UNA sola pregunta cerrada."
                    : $"- Modo de presentación: {constraints.PresentationMode}.");
            }

            if (constraintLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Restricciones de esta etapa:");
                lines.AddRange(constraintLines);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Emite un bloque describiendo las condiciones semánticas que disparan tools especiales.
    /// Solo se incluye para tools que tienen SemanticTriggers definidos y están habilitadas.
    /// </summary>
    internal static string BuildSemanticTriggersBlock(
        IReadOnlyList<IAgentTool> enabledTools,
        AgentConfig config)
    {
        var triggeredTools = enabledTools
            .Where(t => t.SemanticTriggers.Count > 0
                && config.EnabledToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (triggeredTools.Count == 0)
            return string.Empty;

        var lines = new List<string> { "## CUÁNDO USAR HERRAMIENTAS ESPECIALES" };

        foreach (var tool in triggeredTools)
        {
            var conditions = tool.SemanticTriggers.Select(t => t switch
            {
                "customer_frustration"  => "el cliente expresa frustración o enojo",
                "consecutive_errors"    => "hay 2 o más errores consecutivos sin resolución",
                "out_of_scope_request"  => "el cliente pide algo fuera del alcance del bot",
                "explicit_human_request"=> "el cliente pide explícitamente hablar con un humano",
                _                       => t
            });

            lines.Add($"- `{tool.Name}`: Úsalo cuando {string.Join(", o cuando ", conditions)}.");
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
