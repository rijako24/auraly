using System.Globalization;
using System.Text;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Services;

/// <summary>
/// Construye el markdown del catalogo de servicios para inyectar en el LLM.
/// Agrupa por categoria y ordena por Tier (Deluxe > Premium > Base).
/// </summary>
public static class ServiceCatalogBuilder
{
    public static string Build(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<AddOnRuleInfo> addOnRules,
        IReadOnlyList<CategoryInfo> categories,
        bool includeAddOns = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## CATALOGO DE SERVICIOS");
        sb.AppendLine();

        var standardServices = services
            .Where(s => s.IsActive && s.ServiceType == ServiceType.Standard)
            .ToList();

        if (standardServices.Count == 0)
        {
            sb.AppendLine("- No se encontraron servicios principales activos para esta consulta.");
            return sb.ToString().Trim();
        }

        var categoryOrder = categories.OrderBy(c => c.DisplayOrder).ToList();

        var uncategorized = standardServices
            .Where(s => !categoryOrder.Any(c => c.CategoryId == s.CategoryId))
            .OrderByDescending(s => s.Tier)
            .ThenBy(s => s.Name)
            .ToList();

        if (uncategorized.Count > 0)
        {
            sb.AppendLine("### Servicios");
            foreach (var svc in uncategorized)
                AppendService(sb, svc, addOnRules, includeAddOns);
            sb.AppendLine();
        }

        foreach (var cat in categoryOrder)
        {
            var inCategory = standardServices
                .Where(s => s.CategoryId == cat.CategoryId)
                .OrderByDescending(s => s.Tier)
                .ThenBy(s => s.Name)
                .ToList();

            if (inCategory.Count == 0)
                continue;

            sb.AppendLine($"### {cat.Name}");
            if (!string.IsNullOrWhiteSpace(cat.Description))
                sb.AppendLine(cat.Description);
            sb.AppendLine();

            foreach (var svc in inCategory)
                AppendService(sb, svc, addOnRules, includeAddOns);

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static string BuildCategoryOverview(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<CategoryInfo> categories)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## CATEGORIAS DE SERVICIOS");
        sb.AppendLine();

        var standardServices = services
            .Where(s => s.IsActive && s.ServiceType == ServiceType.Standard)
            .ToList();

        if (standardServices.Count == 0)
        {
            sb.AppendLine("- No se encontraron categorias con servicios principales activos.");
            return sb.ToString().Trim();
        }

        var categoryOrder = categories.OrderBy(c => c.DisplayOrder).ToList();

        foreach (var cat in categoryOrder)
        {
            var inCategory = standardServices
                .Where(s => s.CategoryId == cat.CategoryId)
                .OrderByDescending(s => s.Tier)
                .ThenBy(s => s.Name)
                .ToList();

            if (inCategory.Count == 0)
                continue;

            var description = cat.Description;
            if (string.IsNullOrWhiteSpace(description)
                && inCategory.Count == 1
                && string.Equals(inCategory[0].Name, cat.Name, StringComparison.OrdinalIgnoreCase))
            {
                description = inCategory[0].Description;
            }

            AppendCategoryOption(sb, cat.Name, description);
        }

        var uncategorized = standardServices
            .Where(s => !categoryOrder.Any(c => c.CategoryId == s.CategoryId))
            .OrderByDescending(s => s.Tier)
            .ThenBy(s => s.Name)
            .ToList();
        foreach (var svc in uncategorized)
            AppendCategoryOption(sb, svc.Name, svc.Description);

        return sb.ToString().Trim();
    }

    private static void AppendCategoryOption(StringBuilder sb, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine($"- **{name}**");
            return;
        }

        sb.AppendLine($"- **{name}**: {description}");
    }

    private static void AppendService(StringBuilder sb, ServiceInfo svc, IReadOnlyList<AddOnRuleInfo> addOnRules, bool includeAddOns)
    {
        var price = svc.Price.ToString("N0", CultureInfo.InvariantCulture);
        var effectivePrice = svc.EffectivePrice?.ToString("N0", CultureInfo.InvariantCulture);
        var duration = $"{svc.DurationMinutes} min";
        var priceText = svc.EffectivePrice.HasValue && svc.EffectivePrice.Value < svc.Price
            ? $"${effectivePrice} precio promocional (antes ${price})"
            : $"${price}";
        sb.AppendLine($"- **{svc.Name}**: {svc.Description} - {priceText} ({duration})");

        if (!string.IsNullOrWhiteSpace(svc.PromotionSummary))
            sb.AppendLine($"  - Promocion: {svc.PromotionSummary}");

        if (svc.FulfillmentKind == ServiceFulfillmentKind.Enrollment
            && !string.IsNullOrWhiteSpace(svc.FixedScheduleLabel))
        {
            sb.AppendLine($"  - Horario de inscripcion: {svc.FixedScheduleLabel}");
        }

        if (svc.IsBundle && svc.BundleItems.Count > 0)
        {
            foreach (var item in svc.BundleItems)
            {
                var itemPrice = item.Price.ToString("N0", CultureInfo.InvariantCulture);
                sb.AppendLine($"  - {item.Name}: {item.Description} (${itemPrice})");
            }
        }

        if (!includeAddOns)
            return;

        var compatibleAddOns = GetCompatibleAddOns(svc, addOnRules);
        if (compatibleAddOns.Count == 0)
        {
            sb.AppendLine("  - Complementos compatibles: ninguno");
        }
        else
        {
            sb.AppendLine("  - Complementos compatibles:");
            foreach (var rule in compatibleAddOns)
            {
                var addOnPrice = rule.AddOnPrice.ToString("N0", CultureInfo.InvariantCulture);
                var checkoutPolicy = rule.IncludeInCheckoutTotal ? string.Empty : " (precio informativo; no suma al total del checkout)";
                sb.AppendLine($"    - **{rule.AddOnName}**: {rule.AddOnDescription} - ${addOnPrice}{checkoutPolicy}");
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
