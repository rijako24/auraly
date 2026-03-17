using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Construye la sección del catálogo de servicios para el system prompt.
///
/// Estrategia de presentación:
///   1. Solo servicios Standard en el catálogo principal.
///   2. Servicios agrupados por categoría (data-driven), ordenados por Tier dentro de cada categoría.
///   3. Los bundles muestran su composición desde ServiceBundleItems.
///   4. Add-ons: mostrados inmediatamente después de cada categoría de servicios (contexto local para el LLM).
///     Solo ofrecer DESPUÉS de elegir servicio principal; filtrados por selectedCategoryId cuando aplica.
///
/// Multitenant: todo desde datos. Nada hardcodeado.
/// </summary>
public static class ServiceCatalogBuilder
{
    public static string Build(
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules,
        List<CategoryInfo> categories,
        Guid? selectedCategoryId = null)
    {
        var active = services
            .Where(s => s.IsActive && s.ServiceType == ServiceType.Standard)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("## Catálogo de servicios disponibles:");
        sb.AppendLine();

        if (!active.Any())
        {
            sb.AppendLine("_(Sin servicios configurados actualmente)_");
            return sb.ToString().TrimEnd();
        }

        foreach (var category in categories.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name))
        {
            var group = active.Where(s => s.CategoryId == category.CategoryId).ToList();
            if (group.Count == 0)
                continue;

            sb.AppendLine($"### {category.Name}");
            var ordered = group.OrderByDescending(s => (int)s.Tier).ToList();

            foreach (var svc in ordered)
            {
                var header = svc == ordered.First()
                    ? $"#### ⭐ {svc.Name}"
                    : $"#### {svc.Name}";
                sb.AppendLine(header);
                var composition = BuildCompositionLine(svc);
                if (!string.IsNullOrEmpty(composition))
                    sb.AppendLine(composition);
                if (!string.IsNullOrEmpty(svc.Description))
                    sb.AppendLine(svc.Description);
                sb.AppendLine($"_Duración: {svc.DurationMinutes} min | Precio: ${svc.Price:N0}_");
                sb.AppendLine();
            }

            AppendAddOnsForCategory(sb, addOnRules, category.CategoryId, selectedCategoryId);
        }

        AppendUniversalAddOns(sb, addOnRules, selectedCategoryId);

        sb.AppendLine("Solo puedes ofrecer los servicios y servicios extras listados arriba. No inventes ni combinas servicios distintos.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendAddOnsForCategory(
        StringBuilder sb,
        List<AddOnRuleInfo> addOnRules,
        Guid categoryId,
        Guid? selectedCategoryId)
    {
        var addOns = GetAddOnsForCategory(addOnRules, categoryId, selectedCategoryId);
        if (addOns.Count == 0)
            return;

        sb.AppendLine("**Servicios extras para complementar tu plan** (opcionales):");
        sb.AppendLine();
        foreach (var rule in addOns.OrderBy(r => r.DisplayOrder).ThenBy(r => r.AddOnName))
        {
            var compat = BuildCompatibilityText(rule);
            sb.AppendLine($"- **{rule.AddOnName}** — ${rule.AddOnPrice:N0} ({compat})");
            if (!string.IsNullOrEmpty(rule.AddOnDescription))
                sb.AppendLine($"  _{rule.AddOnDescription}_");
        }
        sb.AppendLine();
        sb.AppendLine("Los servicios extras son opcionales.");
        sb.AppendLine();
    }

    private static void AppendUniversalAddOns(
        StringBuilder sb,
        List<AddOnRuleInfo> addOnRules,
        Guid? selectedCategoryId)
    {
        if (selectedCategoryId.HasValue)
            return;

        var universal = addOnRules
            .Where(r => !r.CompatibleCategoryId.HasValue)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.AddOnName)
            .ToList();

        if (universal.Count == 0)
            return;

        sb.AppendLine("**Servicios extras disponibles para cualquier plan** (opcionales):");
        sb.AppendLine();
        foreach (var rule in universal)
        {
            var compat = BuildCompatibilityText(rule);
            sb.AppendLine($"- **{rule.AddOnName}** — ${rule.AddOnPrice:N0} ({compat})");
            if (!string.IsNullOrEmpty(rule.AddOnDescription))
                sb.AppendLine($"  _{rule.AddOnDescription}_");
        }
        sb.AppendLine();
        sb.AppendLine("Los servicios extras son opcionales.");
        sb.AppendLine();
    }

    private static List<AddOnRuleInfo> GetAddOnsForCategory(
        List<AddOnRuleInfo> rules,
        Guid categoryId,
        Guid? selectedCategoryId)
    {
        var filtered = FilterAddOnsByCategory(rules, selectedCategoryId);
        if (selectedCategoryId.HasValue && categoryId != selectedCategoryId.Value)
            return [];

        return filtered.Where(r =>
        {
            if (!r.CompatibleCategoryId.HasValue)
                return selectedCategoryId.HasValue;
            return r.CompatibleCategoryId.Value == categoryId;
        }).ToList();
    }

    private static List<AddOnRuleInfo> FilterAddOnsByCategory(
        List<AddOnRuleInfo> rules,
        Guid? selectedCategoryId)
    {
        if (!selectedCategoryId.HasValue)
            return rules;

        return rules
            .Where(r => IsAddOnCompatibleWithCategory(r, selectedCategoryId.Value))
            .ToList();
    }

    private static bool IsAddOnCompatibleWithCategory(AddOnRuleInfo rule, Guid categoryId)
    {
        if (!rule.CompatibleCategoryId.HasValue)
            return true;

        return rule.CompatibleCategoryId.Value == categoryId;
    }

    private static string BuildCompatibilityText(AddOnRuleInfo rule)
    {
        if (!string.IsNullOrEmpty(rule.CompatibleWithServiceName))
            return $"compatible con: {rule.CompatibleWithServiceName}";

        if (rule.CompatibleCategoryId.HasValue && !string.IsNullOrEmpty(rule.CompatibleCategoryName))
            return $"compatible con servicios {rule.CompatibleCategoryName}";

        return "compatible con todos los servicios anteriores";
    }

    private static string BuildCompositionLine(ServiceInfo svc)
    {
        if (!svc.IsBundle)
            return string.Empty;

        var parts = svc.BundleItems
            .OrderBy(b => b.DisplayOrder)
            .Select(b => b.Name);

        return $"  **Incluye:** {string.Join(" + ", parts)}";
    }
}
