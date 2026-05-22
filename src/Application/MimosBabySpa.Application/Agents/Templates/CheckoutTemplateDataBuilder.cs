using System.Globalization;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Templates;

internal static class CheckoutTemplateDataBuilder
{
    public static Dictionary<string, object?> Build(
        AgentToolContext ctx,
        CheckoutPricingResult checkout,
        string service,
        DateOnly date,
        TimeOnly time,
        string customerName,
        string customerPhone,
        string? linkUrl = null)
    {
        var pricing = checkout.Pricing;
        var policy = checkout.Policy;
        var serviceLine = pricing.LineItems.FirstOrDefault();
        var addonLines = pricing.LineItems
            .Skip(1)
            .Select(li => (object)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = li.Name,
                ["price"] = li.Price.ToString("N0", CultureInfo.InvariantCulture)
            })
            .ToList();

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["service_name"] = service,
            ["service_price"] = (serviceLine?.Price ?? pricing.Total).ToString("N0", CultureInfo.InvariantCulture),
            ["addons"] = addonLines,
            ["total"] = pricing.Total.ToString("N0", CultureInfo.InvariantCulture),
            ["date_formatted"] = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["time"] = time.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["currency"] = string.IsNullOrWhiteSpace(policy.Currency) ? "COP" : policy.Currency,
            ["deposit_pct"] = policy.DepositPercentage,
            ["deposit"] = (checkout.DepositCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
            ["customer_name"] = customerName,
            ["customer_phone"] = customerPhone
        };

        if (!string.IsNullOrWhiteSpace(linkUrl))
            data["link_url"] = linkUrl;

        foreach (var (key, value) in ctx.Facts)
        {
            if (!data.ContainsKey(key))
                data[key] = value;
        }

        return data;
    }
}
