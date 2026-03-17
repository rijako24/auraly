using System.Text;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Generación determinística de resúmenes de reserva (pre-confirmación, post-creación, error de pago).
/// Usa PaymentMethods (Key 5) para medios manuales; PaymentConfig (Key 3) para reglas de anticipo.
/// </summary>
public static class ConfirmationSummaryBuilder
{
    // ─────────────────────────────────────────────────────────────────
    // Pre-confirmación — primera presentación del resumen
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resumen antes del "sí" del usuario. Incluye anticipo + link (si hay) + medios manuales.
    /// </summary>
    public static string BuildPreConfirmationSummary(ConversationState state, LoadedBusinessContext businessContext)
    {
        var sb = new StringBuilder();
        sb.Append($"\n\n📋 *Resumen de tu reserva*\n{BuildSummaryBlock(state, businessContext)}");

        if (businessContext.PaymentConfig is { RequiresAnticipo: true } && !state.PaymentConfirmed)
        {
            sb.Append(BuildAnticipoBlock(state, businessContext));
            if (!string.IsNullOrWhiteSpace(state.PaymentLinkUrl))
                sb.Append("\n\nUna vez confirmado el anticipo, tu reserva quedará asegurada. ¡Estamos para ayudarte!");
            else
                sb.Append("\n\n¿Confirmas la reserva con estos datos?");
        }
        else
        {
            sb.Append("\n\n¿Confirmas la reserva con estos datos?");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Post-creación — reserva ya creada (siempre mostrar resumen)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resumen tras crear la reserva. Incluye anticipo pendiente si aplica.
    /// Cuando PaymentConfirmed=true (webhook/post-pago) NO incluye link ni bloques de pago.
    /// </summary>
    public static string BuildPostCreationSummary(ConversationState state, LoadedBusinessContext businessContext)
    {
        var sb = new StringBuilder();
        sb.Append($"\n\n📋 *Resumen de tu reserva*\n{BuildSummaryBlock(state, businessContext)}");

        if (businessContext.PaymentConfig is { RequiresAnticipo: true } && !state.PaymentConfirmed)
            sb.Append(BuildAnticipoBlock(state, businessContext));

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Error de pago — link falló o sin proveedor, escalar a humano
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resumen cuando el link falló. Muestra medios manuales y mensaje de escalación.
    /// </summary>
    public static string BuildManualPaymentSummary(ConversationState state, LoadedBusinessContext businessContext)
    {
        var sb = new StringBuilder();
        sb.Append($"\n\n📋 *Resumen de tu reserva*\n{BuildSummaryBlock(state, businessContext)}");

        if (businessContext.PaymentConfig is { RequiresAnticipo: true })
        {
            sb.Append(BuildAnticipoBlock(state, businessContext));
            sb.Append("\n\n⚠️ Hubo un inconveniente técnico al generar el link de pago. ");
            sb.Append("Un asesor te contactará para verificar el pago manualmente.");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Reenvío de link — usuario solicitó nuevo link
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bloque de link de pago para reenvío.
    /// </summary>
    public static string BuildPaymentLinkBlock(ConversationState state, LoadedBusinessContext businessContext)
    {
        var (porcentaje, anticipo) = CalculateAnticipo(state, businessContext);

        return $"\n\nPara asegurar tu espacio, solicitamos el anticipo del {porcentaje:P0} del servicio." +
               $"\n\n💳 *Anticipo ({porcentaje:P0}):* ${anticipo:N0}" +
               $"\n\n🔗 Puedes completar tu pago de forma segura accediendo al siguiente enlace:\n{state.PaymentLinkUrl}";
    }

    // ─────────────────────────────────────────────────────────────────
    // Bloque de anticipo + medios de pago (Key 5)
    // ─────────────────────────────────────────────────────────────────

    private static (decimal porcentaje, decimal anticipo) CalculateAnticipo(
        ConversationState state,
        LoadedBusinessContext businessContext)
    {
        var config = businessContext.PaymentConfig;
        var porcentaje = config?.AnticipoPorcentaje ?? 0.50m;
        var anticipo = state.AnticipoAmountInCents.HasValue
            ? state.AnticipoAmountInCents.Value / 100m
            : ReservationTotalCalculator.Calculate(state, businessContext.Services, businessContext.AddOnRules) * porcentaje;
        return (porcentaje, anticipo);
    }

    private static string BuildAnticipoBlock(ConversationState state, LoadedBusinessContext businessContext)
    {
        var (porcentaje, anticipo) = CalculateAnticipo(state, businessContext);
        var config = businessContext.PaymentConfig!;

        var sb = new StringBuilder();
        sb.AppendLine($"\n\n💰 Para confirmar tu reserva, solicitamos un anticipo del {porcentaje:P0} del valor del servicio.");
        sb.AppendLine($"*Anticipo:* ${anticipo:N0} {config.Currency}");

        if (!string.IsNullOrWhiteSpace(state.PaymentLinkUrl))
            sb.AppendLine($"\n🔗 Paga en línea: {state.PaymentLinkUrl}");

        var methodsWithDetails = businessContext.Info.PaymentMethods
            .Where(m => !string.IsNullOrWhiteSpace(m.Details))
            .ToList();

        if (methodsWithDetails.Count > 0)
        {
            sb.AppendLine("\n📱 *Medios de pago:*");
            foreach (var m in methodsWithDetails)
                sb.AppendLine($"  - {m.Icon} {m.Name}: {m.Details}");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Instrucciones al LLM — turnos posteriores al resumen
    // ─────────────────────────────────────────────────────────────────

    public static string BuildInstruction(
        ConversationState state,
        FlowEvaluationResult flowSnapshot,
        LoadedBusinessContext businessContext) =>
        BuildAlreadyPresentedInstruction();

    private static string BuildAlreadyPresentedInstruction() => """
        **ETAPA: CONFIRMACIÓN — El resumen ya fue presentado.**
        Responde al usuario según su mensaje (preguntas, dudas, o confirmación).
        Si confirma explícitamente ("sí", "confirmo", "adelante") → la reserva se procesará.
        PROHIBIDO afirmar "queda confirmada" hasta que el sistema confirme la creación.
        """;

    // ─────────────────────────────────────────────────────────────────
    // Bloque de resumen — campos core, identity, atributos
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSummaryBlock(
        ConversationState state,
        LoadedBusinessContext businessContext,
        IEnumerable<string>? missingFields = null)
    {
        var missing = new HashSet<string>(missingFields ?? [], StringComparer.OrdinalIgnoreCase);
        var rf = businessContext.RequiredFields;
        var attrs = businessContext.Attributes;
        var services = businessContext.Services;
        var addOnRules = businessContext.AddOnRules;

        var sb = new StringBuilder();
        var total = ReservationTotalCalculator.Calculate(state, services, addOnRules);

        foreach (var field in rf.CoreFields)
        {
            var label = FieldLabelResolver.Resolve(field, attrs);
            var value = GetCoreFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        var serviceInfo = services.FirstOrDefault(s =>
            string.Equals(s.Name, state.Service, StringComparison.OrdinalIgnoreCase));
        if (serviceInfo != null)
            sb.AppendLine($"  - Precio servicio: ${serviceInfo.Price:N0}");

        var selectedAddOns = state.GetAttribute("SelectedAddOns");
        if (!string.IsNullOrWhiteSpace(selectedAddOns))
        {
            foreach (var name in selectedAddOns.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()))
            {
                var rule = addOnRules.FirstOrDefault(r =>
                    string.Equals(r.AddOnName, name, StringComparison.OrdinalIgnoreCase));
                sb.AppendLine(rule != null
                    ? $"  - Extra: {rule.AddOnName} — ${rule.AddOnPrice:N0}"
                    : $"  - Extra: {name} — (precio no disponible)");
            }
        }

        if (total > 0)
            sb.AppendLine($"  - **TOTAL: ${total:N0}**");

        foreach (var field in rf.IdentityFields)
        {
            var label = FieldLabelResolver.Resolve(field, attrs);
            var value = GetIdentityFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        foreach (var attrKey in rf.BusinessAttributes)
        {
            var label = FieldLabelResolver.Resolve($"Attribute:{attrKey}", attrs);
            var value = state.GetAttribute(attrKey);
            var fieldKey = $"Attribute:{attrKey}";
            sb.AppendLine($"  - {label}: {ValueOrPending(value, fieldKey, missing)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string? GetCoreFieldValue(ConversationState state, string fieldName) =>
        fieldName switch
        {
            "Service" => state.Service,
            "DesiredDate" => state.DesiredDate?.ToString("dd/MM/yyyy"),
            "DesiredTime" => state.DesiredTime?.ToString("HH:mm"),
            _ => null
        };

    private static string? GetIdentityFieldValue(ConversationState state, string fieldName) =>
        fieldName switch
        {
            "CustomerName" => state.CustomerName,
            "Phone" => state.Phone,
            "Email" => state.Email,
            _ => null
        };

    private static string ValueOrPending(string? value, string fieldKey, HashSet<string> missingFields) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : missingFields.Contains(fieldKey) ? "⚠ pendiente" : "—";
}
