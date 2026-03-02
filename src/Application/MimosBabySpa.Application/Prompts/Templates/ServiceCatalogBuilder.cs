using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Construye la sección del catálogo de servicios para el system prompt.
///
/// Estrategia de presentación:
///   1. Solo servicios Standard en el catálogo principal.
///   2. Servicios agrupados por Category, ordenados por Tier dentro de cada categoría.
///   3. Los bundles muestran su composición desde ServiceBundleItems.
///   4. Add-ons: mostrados inmediatamente después de cada categoría de servicios (contexto local para el LLM).
///     Solo ofrecer DESPUÉS de elegir servicio principal; filtrados por selectedServiceCategory cuando aplica.
///
/// Multitenant: todo desde datos. Nada hardcodeado.
/// </summary>
public static class ServiceCatalogBuilder
{
    private static readonly IReadOnlyDictionary<ServiceCategory, string> CategoryLabels =
        new Dictionary<ServiceCategory, string>
        {
            [ServiceCategory.Plan] = "Planes",
            [ServiceCategory.Taller] = "Talleres",
            [ServiceCategory.Clase] = "Clases",
            [ServiceCategory.Otro] = "Otros servicios"
        };

    public static string Build(
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules,
        ServiceCategory? selectedServiceCategory = null)
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

        // ── Servicios agrupados por categoría, ordenados por Tier ─────────────────
        var byCategory = active
            .Where(s => s.Category != ServiceCategory.Otro)
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key == ServiceCategory.Plan ? 0 : g.Key == ServiceCategory.Taller ? 1 : g.Key == ServiceCategory.Clase ? 2 : 99);

        foreach (var group in byCategory)
        {
            var label = CategoryLabels.GetValueOrDefault(group.Key, group.Key.ToString());
            sb.AppendLine($"### {label}");
            var ordered = group.OrderByDescending(s => (int)s.Tier).ToList();

            foreach (var svc in ordered)
            {
                var header = svc == ordered.First()
                    ? $"#### ⭐ {svc.Name}  ← *RECOMIENDA ESTA PRIMERO*"
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

            AppendAddOnsForCategory(sb, addOnRules, group.Key, selectedServiceCategory);
        }

        // ── Servicios Otro (independientes) ─────────────────
        var otros = active
            .Where(s => s.Category == ServiceCategory.Otro)
            .OrderBy(s => s.Price)
            .ToList();

        foreach (var svc in otros)
        {
            sb.AppendLine($"### {svc.Name}");
            if (!string.IsNullOrEmpty(svc.Description))
                sb.AppendLine(svc.Description);
            sb.AppendLine($"_Duración: {svc.DurationMinutes} min | Precio: ${svc.Price:N0}_");
            sb.AppendLine();
        }

        if (otros.Any())
            AppendAddOnsForCategory(sb, addOnRules, ServiceCategory.Otro, selectedServiceCategory);

        // ── Add-ons compatibles con todos (solo cuando no hay categoría elegida) ─
        AppendUniversalAddOns(sb, addOnRules, selectedServiceCategory);

        sb.AppendLine("Solo puedes ofrecer los servicios y servicios extras listados arriba. No inventes ni combinas servicios distintos.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendAddOnsForCategory(
        StringBuilder sb,
        List<AddOnRuleInfo> addOnRules,
        ServiceCategory category,
        ServiceCategory? selectedServiceCategory)
    {
        var addOns = GetAddOnsForCategory(addOnRules, category, selectedServiceCategory);
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
        ServiceCategory? selectedServiceCategory)
    {
        if (selectedServiceCategory.HasValue)
            return;

        var universal = addOnRules
            .Where(r => !r.CompatibleServiceCategory.HasValue)
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
        ServiceCategory category,
        ServiceCategory? selectedServiceCategory)
    {
        var filtered = FilterAddOnsByCategory(rules, selectedServiceCategory);
        if (selectedServiceCategory.HasValue && category != selectedServiceCategory.Value)
            return [];

        return filtered.Where(r =>
        {
            if (!r.CompatibleServiceCategory.HasValue)
                return selectedServiceCategory.HasValue;
            return r.CompatibleServiceCategory.Value == category;
        }).ToList();
    }

    private static List<AddOnRuleInfo> FilterAddOnsByCategory(
        List<AddOnRuleInfo> rules,
        ServiceCategory? selectedCategory)
    {
        if (!selectedCategory.HasValue)
            return rules;

        return rules
            .Where(r => IsAddOnCompatibleWithCategory(r, selectedCategory.Value))
            .ToList();
    }

    private static bool IsAddOnCompatibleWithCategory(AddOnRuleInfo rule, ServiceCategory category)
    {
        if (!rule.CompatibleServiceCategory.HasValue)
            return true;

        return rule.CompatibleServiceCategory.Value == category;
    }

    private static string BuildCompatibilityText(AddOnRuleInfo rule)
    {
        if (!string.IsNullOrEmpty(rule.CompatibleWithServiceName))
            return $"compatible con: {rule.CompatibleWithServiceName}";

        if (rule.CompatibleServiceCategory.HasValue)
            return $"compatible con servicios {CategoryLabels.GetValueOrDefault(rule.CompatibleServiceCategory.Value, rule.CompatibleServiceCategory.Value.ToString())}";

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
