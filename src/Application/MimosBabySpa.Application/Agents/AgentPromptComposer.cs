using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
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
        var historyList = input.History.ToList();
        var isFirstBotTurn = !historyList.Any(m => IsBotSender(m.Sender));
        var blocks = new List<string>();

        var basePrompt = input.Config.BasePrompt;
        if (!string.IsNullOrWhiteSpace(basePrompt))
            blocks.Add(basePrompt.Trim());

        var eagerBlock = BuildEagerCaptureBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(eagerBlock))
            blocks.Add(eagerBlock);

        var temporalBlock = input.Temporal.ToPromptBlock();
        if (!string.IsNullOrWhiteSpace(temporalBlock))
            blocks.Add(temporalBlock);

        var customerBlock = BuildCustomerBlock(input.Session, input.Engagement, isFirstBotTurn);
        if (!string.IsNullOrWhiteSpace(customerBlock))
            blocks.Add(customerBlock);

        var stateBlock = BuildStateFactsBlock(input.Config, input.Session, input.BookingPolicy, input.LatestPayment);
        if (!string.IsNullOrWhiteSpace(stateBlock))
            blocks.Add(stateBlock);

        var flowBlock = BuildFlowBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(flowBlock))
            blocks.Add(flowBlock);

        var reentryBlock = BuildReentryBlock(input.Config, input.Session);
        if (!string.IsNullOrWhiteSpace(reentryBlock))
            blocks.Add(reentryBlock);

        var actionsBlock = BuildActionsBlock(input);
        if (!string.IsNullOrWhiteSpace(actionsBlock))
            blocks.Add(actionsBlock);

        var turnBlock = BuildTurnContextBlock(input.Config, isFirstBotTurn, input.Engagement);
        if (!string.IsNullOrWhiteSpace(turnBlock))
            blocks.Add(turnBlock);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    internal static string BuildCustomerBlock(
        AgentToolContext? session,
        EngagementContext engagement,
        bool isFirstBotTurn)
    {
        if (engagement != EngagementContext.ReturningCustomer || !isFirstBotTurn)
            return string.Empty;

        var customerName = ConversationFactKeys.Get(session?.Facts, ConversationFactKeys.CustomerName)
            ?? session?.Conversation?.CustomerName;

        var lines = new List<string>
        {
            "## CLIENTE",
            "- regreso_de_cliente: true"
        };

        if (!string.IsNullOrWhiteSpace(customerName))
            lines.Add($"- nombre: {customerName}");

        lines.Add(string.Empty);
        lines.Add("Instrucciones para este turno:");
        lines.Add("- Saluda al cliente por su nombre; no te presentes desde cero.");
        lines.Add("- Lo acordado en conversaciones anteriores ya no aplica: retoma el flujo desde el inicio.");
        lines.Add("- La identidad del cliente ya está disponible; no la pidas ni la confirmes.");

        return string.Join(Environment.NewLine, lines);
    }

    internal static string BuildStateFactsBlock(
        AgentConfig config,
        AgentToolContext? session,
        BookingPolicyParams? bookingPolicy = null,
        PaymentTransaction? latestPayment = null)
    {
        if (session is null && bookingPolicy is null)
            return string.Empty;

        var lines = new List<string> { "## ESTADO ACTUAL" };
        var paymentForContext = ResolvePaymentForContext(session?.ActivePayment, latestPayment);
        var schemaByKey = config.FactSchema
            .ToDictionary(x => x.Key, x => x.Label, StringComparer.OrdinalIgnoreCase);

        if (session is not null)
        {
            var renderedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in config.FactSchema.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = ConversationFactKeys.Get(session.Facts, entry.Key)
                    ?? (session.Facts.TryGetValue(entry.Key, out var raw) ? raw : null);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    var label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Key : entry.Label;
                    lines.Add($"- {label}: {value}");
                    renderedKeys.Add(entry.Key);
                }
            }

            foreach (var (key, value) in session.Facts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (renderedKeys.Contains(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                var label = schemaByKey.TryGetValue(key, out var schemaLabel) && !string.IsNullOrWhiteSpace(schemaLabel)
                    ? schemaLabel
                    : key;
                lines.Add($"- {label}: {value}");
            }

            var reservation = session.ActiveReservation;
            var serviceName = reservation?.Service?.ServiceName
                ?? ConversationFactKeys.Get(session.Facts, ConversationFactKeys.Service);

            if (!string.IsNullOrWhiteSpace(serviceName))
                lines.Add($"- servicio: {serviceName}");

            if (reservation?.ReservationDateTime is not null)
            {
                lines.Add($"- fecha: {DateOnly.FromDateTime(reservation.ReservationDateTime.Value):yyyy-MM-dd}");
                lines.Add($"- hora: {TimeOnly.FromDateTime(reservation.ReservationDateTime.Value):HH:mm}");
            }
            else
            {
                var factDate = ConversationFactKeys.Get(session.Facts, ConversationFactKeys.DesiredDate);
                var factTime = ConversationFactKeys.Get(session.Facts, ConversationFactKeys.DesiredTime);
                if (!string.IsNullOrWhiteSpace(factDate))
                    lines.Add($"- fecha: {factDate}");
                if (!string.IsNullOrWhiteSpace(factTime))
                    lines.Add($"- hora: {factTime}");
            }

            if (reservation is not null)
                lines.Add($"- reserva_estado: {reservation.Status}");

            var customerName = ConversationFactKeys.Get(session.Facts, ConversationFactKeys.CustomerName)
                ?? session.Conversation?.CustomerName;
            lines.Add($"- cliente: {FormatNullable(customerName)}");

            var phone = ConversationContactPhone.Resolve(session.Facts, session.ChannelPhone);
            lines.Add($"- telefono: {FormatNullable(phone)}");

            var email = ConversationFactKeys.Get(session.Facts, ConversationFactKeys.CustomerEmail)
                ?? session.Conversation?.CustomerEmail;
            lines.Add($"- email: {FormatNullable(email)}");

            if (reservation?.Status == ReservationStatus.Confirmed && reservation.ReservationId != Guid.Empty)
                lines.Add($"- reserva_id: {reservation.ReservationId}");

            if (paymentForContext?.RequiresRescheduling == true
                && paymentForContext.Status == PaymentTransactionStatus.Confirmed
                && !paymentForContext.ReservationId.HasValue)
            {
                lines.Add("- pago_confirmado_sin_slot: true");
                lines.Add($"- payment_transaction_id: {paymentForContext.PaymentTransactionId}");
                lines.Add("- accion_requerida: cuando el cliente confirme nuevo horario, usa assign_paid_slot");
            }
        }

        lines.Add(TurnContextPaymentFormatter.FormatDepositLine(bookingPolicy));

        var paymentLine = TurnContextPaymentFormatter.FormatPaymentLine(paymentForContext, bookingPolicy);
        if (!string.IsNullOrWhiteSpace(paymentLine))
            lines.Add(paymentLine);

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Emite una instrucción permanente con los facts que deben capturarse de inmediato
    /// (captureMode=eager) aunque el flujo aún no haya llegado a la etapa que los solicita.
    /// Solo lista los facts que todavía no tienen valor.
    /// </summary>
    internal static string BuildEagerCaptureBlock(AgentConfig config, AgentToolContext? session)
    {
        var eagerMissing = config.FactSchema
            .Where(e => e.CaptureMode.Equals("eager", StringComparison.OrdinalIgnoreCase))
            .Where(e =>
            {
                if (session is null) return true;
                var value = ConversationFactKeys.Get(session.Facts, e.Key)
                    ?? (session.Facts.TryGetValue(e.Key, out var raw) ? raw : null);
                return string.IsNullOrWhiteSpace(value);
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
    /// </summary>
    internal string BuildReentryBlock(AgentConfig config, AgentToolContext? session)
    {
        if (session is null || config.Flow.Stages.Count == 0)
            return string.Empty;

        var snapshots = session.ConversationState.StageFactSnapshots;
        if (snapshots.Count == 0)
            return string.Empty;

        var currentStage = _flowStageDetector.DetectCurrentStage(config.Flow, session);
        var currentIdx = currentStage is null
            ? config.Flow.Stages.Count
            : config.Flow.Stages.ToList().FindIndex(s => s.Id == currentStage.Id);

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
                var current = ConversationFactKeys.Get(session.Facts, factKey)
                    ?? (session.Facts.TryGetValue(factKey, out var raw) ? raw : null);

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

    private string BuildFlowBlock(AgentConfig config, AgentToolContext? session)
    {
        if (config.Flow.Stages.Count == 0)
            return string.Empty;

        var current = _flowStageDetector.DetectCurrentStage(config.Flow, session);
        if (current is null)
            return string.Empty;

        var lines = new List<string>
        {
            "## ETAPA ACTUAL",
            $"- etapa: {current.Id}",
            $"- objetivo: {current.Goal}"
        };

        if (current.SuggestedTools.Count > 0)
            lines.Add($"- acciones_sugeridas: {string.Join(", ", current.SuggestedTools)}");

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
            {
                available.Add(tool.Name);
            }
            else
            {
                blocked.Add($"- {tool.Name}: {eval.BlockReason}");
            }
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

    private static string FormatNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string BuildTurnContextBlock(
        AgentConfig config,
        bool isFirstBotTurn,
        EngagementContext engagement)
    {
        if (!isFirstBotTurn)
        {
            return """
                ## CONTEXTO DE ESTE TURNO

                La conversación ya comenzó: **ya te presentaste** en un turno anterior.

                - NO repitas saludo completo ni presentación.

                - Usa transiciones naturales ("Perfecto", "Entendido", "Claro", etc.).

                """;
        }

        if (engagement == EngagementContext.ReturningCustomer)
        {
            var returningHint = string.IsNullOrWhiteSpace(config.ReturningCustomerGreetingHint)
                ? "Saluda por su nombre de forma cálida (1–2 líneas) y retoma el flujo desde el inicio."
                : $"Plantilla sugerida (adáptala al mensaje del cliente): {config.ReturningCustomerGreetingHint.Trim()}";

            return $"""
                ## CONTEXTO DE ESTE TURNO

                Este es el **primer mensaje** del cliente en esta conversación, pero **ya nos conoce**.

                - Saluda por su nombre; **no** repitas presentación completa de **{config.Name}** ni del negocio.

                - {returningHint}

                - Si el cliente ya pidió algo en su mensaje, responde en el mismo turno.

                """;
        }

        var presentationHint = string.IsNullOrWhiteSpace(config.FirstTurnGreetingHint)
            ? $"Preséntate como **{config.Name}** y saluda al cliente de forma cálida (1–2 líneas)."
            : $"Plantilla sugerida (adáptala al mensaje del cliente): {config.FirstTurnGreetingHint.Trim()}";

        return $"""
            ## CONTEXTO DE ESTE TURNO

            Este es el **primer mensaje** del cliente en esta conversación.

            - Debes saludar y presentarte antes de continuar.

            - {presentationHint}

            - Si el cliente ya pidió algo en su mensaje, saluda brevemente y responde en el mismo turno.

            """;
    }

    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase) ||
        sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);
}
