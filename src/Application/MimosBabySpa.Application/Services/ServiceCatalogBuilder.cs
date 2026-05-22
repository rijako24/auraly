using System.Globalization;
using System.Text;
using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Construye el markdown del catálogo de servicios para inyectar en el LLM.
/// Agrupa por categoría, ordena por Tier (Deluxe > Premium > Base) e incluye complementos compatibles por servicio.
/// </summary>
public static class ServiceCatalogBuilder
{
    public static string Build(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<AddOnRuleInfo> addOnRules,
        IReadOnlyList<CategoryInfo> categories)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## CATÁLOGO DE SERVICIOS");
        sb.AppendLine();

        var standardServices = services
            .Where(s => s.IsActive && s.ServiceType == Domain.Enums.ServiceType.Standard)
            .ToList();

        var categoryOrder = categories.OrderBy(c => c.DisplayOrder).ToList();

        // Servicios sin categoría primero (si los hay)
        var uncategorized = standardServices
            .Where(s => !categoryOrder.Any(c => c.CategoryId == s.CategoryId))
            .OrderByDescending(s => s.Tier)
            .ThenBy(s => s.Name)
            .ToList();

        if (uncategorized.Count > 0)
        {
            sb.AppendLine("### Servicios");
            foreach (var svc in uncategorized)
                AppendService(sb, svc, addOnRules);
            sb.AppendLine();
        }

        // Servicios por categoría
        foreach (var cat in categoryOrder)
        {
            var inCategory = standardServices
                .Where(s => s.CategoryId == cat.CategoryId)
                .OrderByDescending(s => s.Tier)
                .ThenBy(s => s.Name)
                .ToList();

            if (inCategory.Count == 0) continue;

            sb.AppendLine($"### {cat.Name}");
            if (!string.IsNullOrWhiteSpace(cat.Description))
                sb.AppendLine(cat.Description);
            sb.AppendLine();

            foreach (var svc in inCategory)
                AppendService(sb, svc, addOnRules);

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static void AppendService(StringBuilder sb, ServiceInfo svc, IReadOnlyList<AddOnRuleInfo> addOnRules)
    {
        var price = svc.Price.ToString("N0", CultureInfo.InvariantCulture);
        var duration = $"{svc.DurationMinutes} min";
        sb.AppendLine($"- **{svc.Name}**: {svc.Description} — ${price} ({duration})");

        if (svc.IsBundle && svc.BundleItems.Count > 0)
        {
            foreach (var item in svc.BundleItems)
            {
                var itemPrice = item.Price.ToString("N0", CultureInfo.InvariantCulture);
                sb.AppendLine($"  - {item.Name}: {item.Description} (${itemPrice})");
            }
        }

        var compatibleAddOns = GetCompatibleAddOns(svc, addOnRules);
        if (compatibleAddOns.Count > 0)
        {
            sb.AppendLine("  - Complementos compatibles:");
            foreach (var rule in compatibleAddOns)
            {
                var addOnPrice = rule.AddOnPrice.ToString("N0", CultureInfo.InvariantCulture);
                sb.AppendLine($"    - **{rule.AddOnName}**: {rule.AddOnDescription} — ${addOnPrice}");
            }
        }
    }

    private static IReadOnlyList<AddOnRuleInfo> GetCompatibleAddOns(
        ServiceInfo svc,
        IReadOnlyList<AddOnRuleInfo> addOnRules) =>
        addOnRules
            .Where(r => IsCompatibleWithService(r, svc))
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.AddOnName)
            .ToList();

    private static bool IsCompatibleWithService(AddOnRuleInfo rule, ServiceInfo svc)
    {
        if (string.IsNullOrWhiteSpace(rule.CompatibleWithServiceName))
            return true;

        return string.Equals(rule.CompatibleWithServiceName, svc.Name, StringComparison.OrdinalIgnoreCase);
    }
}
