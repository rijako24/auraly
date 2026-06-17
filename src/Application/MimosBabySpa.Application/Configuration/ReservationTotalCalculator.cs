namespace MimosBabySpa.Application.Configuration;

public static class ReservationTotalCalculator
{
    public static decimal Calculate(
        string serviceName,
        string? selectedAddOnsCsv,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        decimal total = 0;

        var serviceInfo = services.FirstOrDefault(s =>
            string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));
        if (serviceInfo != null)
            total = serviceInfo.Price;

        if (string.IsNullOrWhiteSpace(selectedAddOnsCsv))
            return total;

        foreach (var name in selectedAddOnsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rule = addOnRules.FirstOrDefault(r =>
                string.Equals(r.AddOnName, name, StringComparison.OrdinalIgnoreCase));
            if (rule != null && rule.IncludeInCheckoutTotal)
                total += rule.AddOnPrice;
        }

        return total;
    }
}
