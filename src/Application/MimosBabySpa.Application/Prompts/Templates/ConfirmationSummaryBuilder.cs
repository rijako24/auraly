using System.Text;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Dos responsabilidades para la etapa ConfirmingBooking:
///
///   1. BuildInstruction: instrucciones al LLM para turnos posteriores al resumen
///      (cuando el usuario responde, confirma, o si hubo error de pago).
///      La primera presentación del resumen se maneja determinísticamente en FASE 5
///      del orquestador — el LLM NO participa.
///
///   2. BuildInjectableSummary: genera el bloque completo de resumen + cierre para
///      inyección programática (TryBuildDeterministicResponse, FASE 5).
///      - Sin anticipo: resumen + "¿Confirmas la reserva con estos datos?"
///      - Con anticipo: resumen + bloque de pago (monto anticipo + link).
///      Garantiza integridad transaccional sin depender del LLM.
///
/// DISEÑO MULTITENANT:
///   - Itera CoreFields, IdentityFields y BusinessAttributes desde RequiredFieldsConfiguration.
///   - Usa AttributeDefinition.DisplayName para etiquetas de atributos del negocio.
///   - No hardcodea nombres de negocio ni flujos específicos.
/// </summary>
public static class ConfirmationSummaryBuilder
{
    // ─────────────────────────────────────────────────────────────────
    // Resumen inyectable — bloque completo de confirmación (FASE 5.5)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera el bloque completo de confirmación para inyección programática (FASE 5.5).
    /// Incluye resumen de datos + cierre apropiado según configuración de pago:
    ///   - Sin anticipo: "¿Confirmas la reserva con estos datos?"
    ///   - Con anticipo: bloque de pago (monto + link).
    /// </summary>
    public static string BuildInjectableSummary(ConversationState state, LoadedBusinessContext businessContext)
    {
        var summary = BuildSummaryBlock(
            state,
            businessContext.RequiredFields,
            businessContext.Attributes,
            businessContext.Services,
            businessContext.AddOnRules,
            missingFields: []);

        var sb = new StringBuilder();
        sb.Append($"\n\n📋 *Resumen de tu reserva*\n{summary}");

        if (businessContext.PaymentConfig is { RequiresAnticipo: true } paymentConfig
            && !string.IsNullOrWhiteSpace(state.PaymentLinkUrl))
        {
            sb.Append(BuildPaymentLinkBlock(state, businessContext));
        }
        else
        {
            sb.Append("\n\n¿Confirmas la reserva con estos datos?");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Genera el bloque de link de pago (monto + URL) para inyección programática.
    /// Usado tanto en BuildInjectableSummary (primera presentación) como en reenvío de link.
    /// </summary>
    public static string BuildPaymentLinkBlock(ConversationState state, LoadedBusinessContext businessContext)
    {
        var paymentConfig = businessContext.PaymentConfig;
        var porcentaje = paymentConfig?.AnticipoPorcentaje ?? 0.50m;
        var anticipo = state.AnticipoAmountInCents.HasValue
            ? state.AnticipoAmountInCents.Value / 100m
            : ReservationTotalCalculator.Calculate(state, businessContext.Services, businessContext.AddOnRules) * porcentaje;

        return $"\n\nPara confirmar tu reserva, necesitas realizar el pago del anticipo ({porcentaje:P0})." +
               $"\n\n💳 *Anticipo ({porcentaje:P0}):* ${anticipo:N0}" +
               $"\n\n🔗 Puedes completar tu pago de forma segura accediendo al siguiente enlace:\n{state.PaymentLinkUrl}";
    }

    // ─────────────────────────────────────────────────────────────────
    // Instrucciones al LLM — solo para turnos POSTERIORES al resumen
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instrucciones al LLM para turnos donde el resumen ya fue presentado,
    /// o cuando hubo un error técnico al generar el link de pago.
    /// La primera presentación del resumen se maneja determinísticamente (sin LLM).
    /// </summary>
    public static string BuildInstruction(
        ConversationState state,
        FlowEvaluationResult flowSnapshot,
        LoadedBusinessContext businessContext)
    {
        if (!state.ConfirmationSummaryPresented
            && businessContext.PaymentConfig is { RequiresAnticipo: true }
            && string.IsNullOrWhiteSpace(state.PaymentLinkUrl))
        {
            return BuildPaymentLinkErrorInstruction();
        }

        return BuildAlreadyPresentedInstruction();
    }

    private static string BuildAlreadyPresentedInstruction() => """
        **ETAPA: CONFIRMACIÓN — El resumen ya fue presentado.**
        Responde al usuario según su mensaje (preguntas, dudas, o confirmación).
        Si confirma explícitamente ("sí", "confirmo", "adelante") → la reserva se procesará.
        PROHIBIDO afirmar "queda confirmada" hasta que el sistema confirme la creación.
        """;

    private static string BuildPaymentLinkErrorInstruction() => """
        **ETAPA: CONFIRMACIÓN — Error técnico al generar link de pago.**
        Informa que hubo un inconveniente al preparar el pago.
        Pide al usuario que intente de nuevo enviando un mensaje.
        PROHIBIDO mostrar resumen de datos ni afirmar que la reserva está lista.
        """;

    // ─────────────────────────────────────────────────────────────────
    // Bloque de resumen — itera campos en orden: core → identity → atributos
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSummaryBlock(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields,
        IReadOnlyDictionary<string, AttributeDefinition> attributeDefinitions,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules,
        IEnumerable<string> missingFields)
    {
        var missing = new HashSet<string>(missingFields, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        var total = ReservationTotalCalculator.Calculate(state, services, addOnRules);

        foreach (var field in requiredFields.CoreFields)
        {
            var label = FieldLabelResolver.Resolve(field, attributeDefinitions);
            var value = GetCoreFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        // Precio del servicio principal
        var serviceInfo = services.FirstOrDefault(s =>
            string.Equals(s.Name, state.Service, StringComparison.OrdinalIgnoreCase));
        if (serviceInfo != null)
            sb.AppendLine($"  - Precio servicio: ${serviceInfo.Price:N0}");

        var selectedAddOns = state.GetAttribute("SelectedAddOns");
        if (!string.IsNullOrWhiteSpace(selectedAddOns))
        {
            var addOnNames = selectedAddOns
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim());

            foreach (var name in addOnNames)
            {
                var rule = addOnRules.FirstOrDefault(r =>
                    string.Equals(r.AddOnName, name, StringComparison.OrdinalIgnoreCase));
                if (rule != null)
                    sb.AppendLine($"  - Add-on {rule.AddOnName}: ${rule.AddOnPrice:N0}");
                else
                    sb.AppendLine($"  - Add-on {name}: (precio no disponible)");
            }
        }

        if (total > 0)
            sb.AppendLine($"  - **TOTAL: ${total:N0}**");

        foreach (var field in requiredFields.IdentityFields)
        {
            var label = FieldLabelResolver.Resolve(field, attributeDefinitions);
            var value = GetIdentityFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        foreach (var attrKey in requiredFields.BusinessAttributes)
        {
            var label = FieldLabelResolver.Resolve($"Attribute:{attrKey}", attributeDefinitions);
            var value = state.GetAttribute(attrKey);
            var fieldKey = $"Attribute:{attrKey}";
            sb.AppendLine($"  - {label}: {ValueOrPending(value, fieldKey, missing)}");
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────

    private static string? GetCoreFieldValue(ConversationState state, string fieldName) =>
        fieldName switch
        {
            "Service"     => state.Service,
            "DesiredDate" => state.DesiredDate?.ToString("dd/MM/yyyy"),
            "DesiredTime" => state.DesiredTime?.ToString("HH:mm"),
            _             => null
        };

    private static string? GetIdentityFieldValue(ConversationState state, string fieldName) =>
        fieldName switch
        {
            "CustomerName" => state.CustomerName,
            "Phone"        => state.Phone,
            "Email"        => state.Email,
            _              => null
        };

    private static string ValueOrPending(string? value, string fieldKey, HashSet<string> missingFields) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : missingFields.Contains(fieldKey) ? "⚠ pendiente" : "—";
}
