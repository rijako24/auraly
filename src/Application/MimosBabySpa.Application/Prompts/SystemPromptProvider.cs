using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts.Templates;
using MimosBabySpa.Domain.Enums;
using System.Text;

namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Construye el system prompt para la generación de respuesta conversacional.
///
/// Diseño lean (~800 tokens vs ~3,250 anterior):
/// - Identidad dinámica por tenant: texto libre desde BusinessConfiguration key=Personality,
///   o campos estructurados de BusinessPersonality como fallback.
/// - 5 principios condensados en ~80 tokens.
/// - Información del negocio y catálogo de servicios: dinámicos desde tablas normalizadas.
///
/// Multitenant: 100% parametrizado. Nada hardcodeado.
/// </summary>
public class SystemPromptProvider : IPromptProvider
{
    public Task<string> BuildAsync(
        SystemPromptInput input,
        CancellationToken cancellationToken = default)
    {
        var context = input.BusinessContext;
        var sb = new StringBuilder();

        // ── Identidad y rol (dinámico por tenant) ─────────────────
        sb.AppendLine(BuildRoleSection(context));
        sb.AppendLine();

        // ── Principios condensados (5 → 5 líneas, no 600 tokens) ──
        sb.AppendLine(BuildPrinciplesSection());
        sb.AppendLine();

        // ── Información del negocio (dinámica) ────────────────────
        sb.AppendLine(BuildBusinessSection(context));
        sb.AppendLine();

        // ── Catálogo de servicios (dinámico, agrupado por categoría y tier) ──────
        sb.AppendLine(ServiceCatalogBuilder.Build(context.Services, context.AddOnRules, input.SelectedServiceCategory));
        sb.AppendLine();

        // ── Atributos específicos del negocio (dinámico) ────────
        var attributesSection = BuildAttributesSection(context);
        if (!string.IsNullOrEmpty(attributesSection))
        {
            sb.AppendLine(attributesSection);
            sb.AppendLine();
        }

        // ── Estrategia de ventas (opcional, por tenant) ───────────
        if (!string.IsNullOrEmpty(context.SalesStrategy))
        {
            sb.AppendLine(BuildSalesStrategySection(context.SalesStrategy));
            sb.AppendLine();
        }

        // ── Regla de oro (siempre) ────────────────────────────────
        sb.AppendLine(BuildGoldenRules(context.Personality));

        return Task.FromResult(sb.ToString().Trim());
    }

    // ─────────────────────────────────────────────────────────────────
    // Builders privados
    // ─────────────────────────────────────────────────────────────────

    private static string BuildRoleSection(LoadedBusinessContext context)
    {
        // Texto libre configurado por el tenant → se inyecta directamente sin template
        if (!string.IsNullOrWhiteSpace(context.Personality.SystemIdentityText))
            return context.Personality.SystemIdentityText;

        // Fallback: construir identidad desde campos estructurados de BusinessPersonality
        var expertise = !string.IsNullOrEmpty(context.Personality.Expertise)
            ? $", {context.Personality.Expertise}"
            : ", asistente virtual";

        var tone = context.Personality.Tone.Any()
            ? $"**Tono:** {string.Join(", ", context.Personality.Tone)}."
            : string.Empty;

        return RoleTemplate.Template
            .Replace("{ASSISTANT_NAME}", context.Personality.AssistantName)
            .Replace("{EXPERTISE_CLAUSE}", expertise)
            .Replace("{BUSINESS_NAME}", context.Info.Name)
            .Replace("{TONE_CLAUSE}", tone);
    }

    private static string BuildPrinciplesSection() => """
        ## Principios (guían todas tus respuestas):
        1. **VERACIDAD** — Solo afirmas lo que ves en los datos del sistema. Si algo no está en el catálogo, no existe.
        2. **EMPATÍA** — Entiende primero, recomienda después. Una pregunta a la vez. Contextualiza tus recomendaciones.
        3. **RESPETO** — Usa la información que ya tienes. NUNCA re-preguntes algo ya respondido. Lee el estado completo antes de responder.
        4. **TRANSPARENCIA** — Verifica disponibilidad antes de prometarla. Pide confirmación explícita antes de crear reservas.
        5. **UTILIDAD** — Cada respuesta debe ayudar al cliente a avanzar. Guía hacia el siguiente paso concreto.
        """;

    private static string BuildBusinessSection(LoadedBusinessContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Información del negocio:");
        sb.AppendLine($"- **Nombre:** {context.Info.Name}");

        if (!string.IsNullOrEmpty(context.Info.Description))
            sb.AppendLine($"- **Descripción:** {context.Info.Description}");
        if (!string.IsNullOrEmpty(context.Info.Address))
            sb.AppendLine($"- **Dirección:** {context.Info.Address}");

        // Contacto
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(context.Info.Phone))   contactParts.Add($"Tel: {context.Info.Phone}");
        if (!string.IsNullOrEmpty(context.Info.Email))   contactParts.Add($"Email: {context.Info.Email}");
        if (!string.IsNullOrEmpty(context.Info.Website)) contactParts.Add($"Web: {context.Info.Website}");
        if (contactParts.Any())
            sb.AppendLine($"- **Contacto:** {string.Join(" | ", contactParts)}");

        // Horarios
        if (context.Info.Schedule.Any())
        {
            sb.AppendLine("- **Horarios:**");
            foreach (var (day, blocks) in context.Info.Schedule.OrderBy(x => GetDayOrder(x.Key)))
            {
                if (blocks == null || !blocks.Any())
                    sb.AppendLine($"  - {day}: Cerrado");
                else
                    sb.AppendLine($"  - {day}: {string.Join(" y ", blocks.Select(b => $"{b.Open}–{b.Close}"))}");
            }
        }

        // Métodos de pago
        if (context.Info.PaymentMethods.Any())
            sb.AppendLine($"- **Pagos:** {string.Join(", ", context.Info.PaymentMethods.Select(p => p.Name))}");

        return sb.ToString().TrimEnd();
    }

    private static string BuildSalesStrategySection(string strategy)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Estrategia de recomendación y venta:");
        sb.AppendLine(strategy);
        return sb.ToString().TrimEnd();
    }

    private static string BuildAttributesSection(LoadedBusinessContext context)
    {
        var required = context.Attributes.Where(a => a.Value.IsRequired).ToList();
        var optional = context.Attributes.Where(a => !a.Value.IsRequired).ToList();

        if (!required.Any() && !optional.Any())
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Información adicional que debes recopilar:");

        if (required.Any())
        {
            sb.AppendLine("**Obligatoria** (pregunta de forma natural, una a la vez):");
            foreach (var (key, attr) in required)
                sb.AppendLine($"- **{attr.DisplayName}**: {attr.Description}");
        }

        if (optional.Any())
        {
            sb.AppendLine("**Opcional** (pregunta solo si surge naturalmente):");
            foreach (var (key, attr) in optional)
                sb.AppendLine($"- **{attr.DisplayName}**: {attr.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildGoldenRules(BusinessPersonality personality) => $"""
        ## Reglas críticas de respuesta:
        - Respuestas BREVES (3-4 líneas máximo). Una pregunta a la vez.
        - **CIERRES VARIADOS** — NO siempre termines con pregunta. Eso atosiga. Varía:
          → A veces: pregunta concreta (cuando necesites un dato para avanzar).
          → Otras veces: comentario cálido ("Cuando quieras más detalles, aquí estoy") o dato útil sin pregunta.
          En respuestas meramente informativas (exploración, saludos, más info), al menos 1 de cada 3 puede cerrar SIN pregunta.
        - NUNCA digas que vas a crear/confirmar algo que el sistema aún no ejecutó.
        - Si hay horarios disponibles confirmados → MUÉSTRALOS todos explícitamente.
        - Si el usuario eligió un horario → Confirma y pregunta "¿Confirmo tu reserva?".
        - Si el usuario confirmó reserva y el sistema la creó → Celebra y ofrece ayuda adicional.
        - Usa el historial de conversación para NO repetir información ya compartida.
        """;

    private static int GetDayOrder(string day) =>
        Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var d)
            ? (d == DayOfWeek.Sunday ? 7 : (int)d)
            : 999;
}
