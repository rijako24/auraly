using System.Text;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Genera el bloque de instrucciones para el LLM de respuesta cuando el flujo está en
/// la etapa de confirmación (TransactionStage.ConfirmingBooking).
/// Por invariante del FlowEngine, solo se llama cuando todos los datos están completos.
///
/// DISEÑO MULTITENANT:
///   - Itera CoreFields, IdentityFields y BusinessAttributes desde RequiredFieldsConfiguration.
///   - Usa AttributeDefinition.DisplayName para etiquetas de atributos del negocio.
///   - No hardcodea nombres de negocio ni flujos específicos.
///   - El mapeo de campos core (Service/Date/Time/etc.) a etiquetas de display es la
///     única responsabilidad dominio-específica de esta clase, ya que esos campos son
///     propiedades tipadas de ConversationState (no un diccionario).
/// </summary>
public static class ConfirmationSummaryBuilder
{
    /// <summary>
    /// Etiquetas de display para los campos tipados de ConversationState.
    /// Los atributos de negocio usan AttributeDefinition.DisplayName (dinámico por tenant).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CoreFieldLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Service"]      = "Servicio",
            ["DesiredDate"]  = "Fecha",
            ["DesiredTime"]  = "Hora",
            ["CustomerName"] = "Nombre del cliente",
            ["Phone"]        = "Teléfono",
            ["Email"]        = "Email"
        };

    // ─────────────────────────────────────────────────────────────────
    // Punto de entrada — dispatcha según si todos los datos están listos
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera la instrucción de confirmación. Solo debe llamarse cuando CurrentStage == ConfirmingBooking.
    /// Por invariante: en ConfirmingBooking todos los campos están completos.
    /// </summary>
    public static string BuildInstruction(
        ConversationState state,
        FlowEvaluationResult flowSnapshot,
        LoadedBusinessContext businessContext)
    {
        return BuildReadyForConfirmationInstruction(state, businessContext);
    }

    // ─────────────────────────────────────────────────────────────────
    // Todos los datos completos — pedir confirmación final
    // ─────────────────────────────────────────────────────────────────

    private static string BuildReadyForConfirmationInstruction(
        ConversationState state,
        LoadedBusinessContext businessContext)
    {
        var summary = BuildSummaryBlock(
            state,
            businessContext.RequiredFields,
            businessContext.Attributes,
            businessContext.Services,
            businessContext.AddOnRules,
            missingFields: []);

        return $"""
            **ETAPA: CONFIRMACIÓN FINAL — Todos los datos están completos y la disponibilidad ha sido verificada.**
            Presenta al cliente el siguiente resumen de su reserva:
            {summary}
            Pregunta EXPLÍCITAMENTE si confirma (p. ej. "¿Confirmas la reserva con estos datos?").
            PROHIBIDO afirmar "queda confirmada", "listo", "agendado" o equivalentes hasta recibir la confirmación del cliente.
            """;
    }

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
        decimal total = 0;

        foreach (var field in requiredFields.CoreFields)
        {
            var label = GetCoreFieldLabel(field);
            var value = GetCoreFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        // Precio del servicio principal
        var serviceInfo = services.FirstOrDefault(s =>
            string.Equals(s.Name, state.Service, StringComparison.OrdinalIgnoreCase));
        if (serviceInfo != null)
        {
            sb.AppendLine($"  - Precio servicio: ${serviceInfo.Price:N0}");
            total = serviceInfo.Price;
        }

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
                {
                    sb.AppendLine($"  - Add-on {rule.AddOnName}: ${rule.AddOnPrice:N0}");
                    total += rule.AddOnPrice;
                }
                else
                {
                    sb.AppendLine($"  - Add-on {name}: (precio no disponible)");
                }
            }
        }

        if (total > 0)
            sb.AppendLine($"  - **TOTAL: ${total:N0}**");

        foreach (var field in requiredFields.IdentityFields)
        {
            var label = GetCoreFieldLabel(field);
            var value = GetIdentityFieldValue(state, field);
            sb.AppendLine($"  - {label}: {ValueOrPending(value, field, missing)}");
        }

        foreach (var attrKey in requiredFields.BusinessAttributes)
        {
            var definition = attributeDefinitions.GetValueOrDefault(attrKey);
            var label = !string.IsNullOrWhiteSpace(definition?.DisplayName)
                ? definition.DisplayName
                : attrKey;
            var value = state.GetAttribute(attrKey);
            var fieldKey = $"Attribute:{attrKey}";
            sb.AppendLine($"  - {label}: {ValueOrPending(value, fieldKey, missing)}");
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────

    private static string GetCoreFieldLabel(string fieldName) =>
        CoreFieldLabels.TryGetValue(fieldName, out var label) ? label : fieldName;

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
