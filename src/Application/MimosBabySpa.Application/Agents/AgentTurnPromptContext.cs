using System.Globalization;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

public static class AgentTurnPromptContext
{
    public static string AppendTurnContext(
        string systemPrompt,
        AgentConfig config,
        IEnumerable<Message> history,
        TemporalReferenceContext temporal,
        AgentToolContext? session = null,
        BookingPolicyParams? bookingPolicy = null,
        PaymentTransaction? latestPayment = null)
    {
        var historyList = history.ToList();
        return AppendTurnContext(
            systemPrompt, config, IsFirstBotTurn(historyList), temporal, session, bookingPolicy, latestPayment);
    }

    private static bool IsFirstBotTurn(IEnumerable<Message> history) =>
        !history.Any(m => IsBotSender(m.Sender));

    private static string AppendTurnContext(
        string systemPrompt,
        AgentConfig config,
        bool isFirstBotTurn,
        TemporalReferenceContext temporal,
        AgentToolContext? session,
        BookingPolicyParams? bookingPolicy,
        PaymentTransaction? latestPayment)
    {
        var blocks = new List<string>();

        var temporalBlock = temporal.ToPromptBlock();
        if (!string.IsNullOrWhiteSpace(temporalBlock))
            blocks.Add(temporalBlock);

        var stateBlock = BuildStateFactsBlock(session, bookingPolicy, latestPayment);
        if (!string.IsNullOrWhiteSpace(stateBlock))
            blocks.Add(stateBlock);

        var turnBlock = BuildTurnContextBlock(config, isFirstBotTurn);
        if (!string.IsNullOrWhiteSpace(turnBlock))
            blocks.Add(turnBlock);

        if (blocks.Count == 0)
            return systemPrompt;

        var dynamicContext = string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
        return string.IsNullOrWhiteSpace(systemPrompt)
            ? dynamicContext
            : $"{systemPrompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{dynamicContext}";
    }

    internal static string BuildStateFactsBlock(
        AgentToolContext? session,
        BookingPolicyParams? bookingPolicy = null,
        PaymentTransaction? latestPayment = null)
    {
        if (session is null && bookingPolicy is null)
            return string.Empty;

        var lines = new List<string> { "## ESTADO ACTUAL" };
        var paymentForContext = ResolvePaymentForContext(session?.ActivePayment, latestPayment);

        if (session is not null)
        {
            foreach (var (key, value) in session.Facts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(value))
                    lines.Add($"- {key}: {value}");
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

    private static string BuildTurnContextBlock(AgentConfig config, bool isFirstBotTurn)
    {
        if (isFirstBotTurn)
        {
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

        return """
            ## CONTEXTO DE ESTE TURNO

            La conversación ya comenzó: **ya te presentaste** en un turno anterior.

            - NO repitas saludo completo ni presentación.

            - Usa transiciones naturales ("Perfecto", "Entendido", "Claro", etc.).

            """;
    }

    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase) ||
        sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);
}
