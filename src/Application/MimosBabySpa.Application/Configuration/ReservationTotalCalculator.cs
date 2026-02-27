using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Calcula el total de una reserva (servicio principal + add-ons).
/// Fuente única de verdad — DRY entre ConfirmationSummaryBuilder y orquestador de pago.
/// </summary>
public static class ReservationTotalCalculator
{
    /// <summary>
    /// Calcula el total en la moneda del negocio (pesos).
    /// </summary>
    public static decimal Calculate(
        ConversationState state,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        decimal total = 0;

        var serviceInfo = services.FirstOrDefault(s =>
            string.Equals(s.Name, state.Service, StringComparison.OrdinalIgnoreCase));
        if (serviceInfo != null)
            total = serviceInfo.Price;

        var selectedAddOns = state.GetAttribute("SelectedAddOns");
        if (string.IsNullOrWhiteSpace(selectedAddOns))
            return total;

        var addOnNames = selectedAddOns
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim());

        foreach (var name in addOnNames)
        {
            var rule = addOnRules.FirstOrDefault(r =>
                string.Equals(r.AddOnName, name, StringComparison.OrdinalIgnoreCase));
            if (rule != null)
                total += rule.AddOnPrice;
        }

        return total;
    }
}
