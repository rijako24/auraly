using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Constants;
using MimosBabySpa.Application.Prompts.Core;
using MimosBabySpa.Application.Prompts.Examples;
using MimosBabySpa.Application.Prompts.Process;
using MimosBabySpa.Application.Prompts.Templates;
using System.Text;

namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Proveedor de prompts del sistema v2.0.
/// Arquitectura basada en principios, no en reglas.
/// 
/// FILOSOFÍA:
/// - Principios fundamentales > Reglas específicas
/// - Comportamientos positivos > Restricciones negativas  
/// - Genérico y multi-tenant > Hardcoded
/// - Clean y organizado > Monolítico
/// 
/// CAMBIOS PRINCIPALES:
/// - Reemplaza 40+ reglas negativas por 5 principios fundamentales
/// - Define comportamientos positivos en vez de restricciones
/// - Auto-reflexión pre-respuesta (Constitutional AI)
/// - 100% genérico y escalable a cualquier negocio
/// </summary>
public class SystemPromptProvider : IPromptProvider
{
    public Task<string> BuildAsync(
        LoadedBusinessContext context,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // ═══════════════════════════════════════════════════════════
        // PARTE 1: IDENTIDAD Y PERSONALIDAD
        // Quién eres, tu misión, tu tono
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(BuildRoleSection(context));
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 2: PRINCIPIOS FUNDAMENTALES (Core del sistema)
        // Los 5 principios que guían TODAS las decisiones
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(SalesPrinciples.All);
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 3: COMPORTAMIENTOS HUMANOS (Cómo actuar)
        // Comportamientos observables de un vendedor profesional
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(HumanBehaviors.All);
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 4: EJEMPLOS DE CONVERSACIÓN (Few-Shot Learning)
        // Ejemplos concretos de conversación correcta e incorrecta
        // Estrategia: "Show, Don't Tell" - Más efectivo que instrucciones
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(ConversationExamples.All);
        sb.AppendLine();
        
        sb.AppendLine(AntiPatternExamples.All);
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 5: INFORMACIÓN DEL NEGOCIO (Datos concretos)
        // Todo lo que necesitas saber sobre este negocio específico
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(BuildBusinessInformationSection(context));
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 6: CATÁLOGO DE SERVICIOS (Lo que puedes ofrecer)
        // Lista completa de servicios disponibles con descripciones
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(BuildSystemConstraintsSection(context));
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════
        // PARTE 7: GUÍA DE VENTAS ESPECÍFICA (Si aplica)
        // Recomendaciones específicas del negocio
        // ═══════════════════════════════════════════════════════════
        
        if (context.SalesGuidance.IsEnabled && !string.IsNullOrEmpty(context.SalesGuidance.GuidanceText))
        {
            sb.AppendLine(BuildSalesGuidanceSection(context.SalesGuidance));
            sb.AppendLine();
        }

        // ═══════════════════════════════════════════════════════════
        // PARTE 8: REFLEXIÓN PRE-RESPUESTA (Constitutional AI)
        // Checklist interno para auto-corrección antes de responder
        // ═══════════════════════════════════════════════════════════
        
        sb.AppendLine(ReflectionChecklist.All);

        return Task.FromResult(sb.ToString());
    }

    // ═══════════════════════════════════════════════════════════
    // BUILDERS PRIVADOS (Clean, organizados, reutilizables)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Carga el template de rol y reemplaza placeholders con datos dinámicos.
    /// Backend SOLO carga, NO genera contenido.
    /// </summary>
    private string BuildRoleSection(LoadedBusinessContext context)
    {
        // Construir cláusula de expertise
        var expertiseClause = !string.IsNullOrEmpty(context.Personality.Expertise)
            ? $", {context.Personality.Expertise}"
            : ", asistente virtual";

        // Construir cláusula de tono (opcional)
        var toneClause = context.Personality.Tone.Any()
            ? $"**Tu tono es:** {string.Join(", ", context.Personality.Tone)}.\n"
            : string.Empty;

        // CARGAR template y REEMPLAZAR placeholders
        return RoleTemplate.Template
            .Replace("{ASSISTANT_NAME}", context.Personality.AssistantName)
            .Replace("{EXPERTISE_CLAUSE}", expertiseClause)
            .Replace("{BUSINESS_NAME}", context.Info.Name)
            .Replace("{TONE_CLAUSE}", toneClause.TrimEnd());
    }


    /// <summary>
    /// Carga el template de información del negocio y reemplaza placeholders.
    /// Backend SOLO carga, NO genera contenido.
    /// </summary>
    private string BuildBusinessInformationSection(LoadedBusinessContext context)
    {
        // Construir secciones opcionales
        var descriptionSection = !string.IsNullOrEmpty(context.Info.Description)
            ? BusinessInfoTemplate.DescriptionSection.Replace("{DESCRIPTION}", context.Info.Description)
            : string.Empty;

        var addressSection = !string.IsNullOrEmpty(context.Info.Address)
            ? BusinessInfoTemplate.AddressSection.Replace("{ADDRESS}", context.Info.Address)
            : string.Empty;

        var contactSection = BuildContactSection(context.Info);
        var scheduleSection = BuildScheduleSection(context.Info.Schedule);
        var paymentMethodsSection = BuildPaymentMethodsSection(context.Info.PaymentMethods);

        // CARGAR template y REEMPLAZAR placeholders
        return BusinessInfoTemplate.Template
            .Replace("{BUSINESS_NAME}", context.Info.Name)
            .Replace("{DESCRIPTION_SECTION}", descriptionSection)
            .Replace("{ADDRESS_SECTION}", addressSection)
            .Replace("{CONTACT_SECTION}", contactSection)
            .Replace("{SCHEDULE_SECTION}", scheduleSection)
            .Replace("{PAYMENT_METHODS_SECTION}", paymentMethodsSection)
            .Replace("\n\n\n", "\n\n") // Limpiar líneas vacías múltiples
            .Trim();
    }

    /// <summary>
    /// Construye la sección de contacto si hay datos disponibles.
    /// </summary>
    private string BuildContactSection(BusinessInfo info)
    {
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(info.Phone)) contactParts.Add($"Tel: {info.Phone}");
        if (!string.IsNullOrEmpty(info.Email)) contactParts.Add($"Email: {info.Email}");
        if (!string.IsNullOrEmpty(info.Website)) contactParts.Add($"Web: {info.Website}");

        return contactParts.Any()
            ? BusinessInfoTemplate.ContactSection.Replace("{CONTACT_INFO}", string.Join(" | ", contactParts))
            : string.Empty;
    }

    /// <summary>
    /// Construye la sección de horarios usando el template.
    /// </summary>
    private string BuildScheduleSection(Dictionary<string, List<TimeBlock>> schedule)
    {
        if (!schedule.Any())
            return string.Empty;

        var scheduleItems = new StringBuilder();
        var orderedSchedule = schedule.OrderBy(x => GetDayOfWeekOrder(x.Key));

        foreach (var (day, blocks) in orderedSchedule)
        {
            var dayName = LocalizationConstants.DayNames.Get(day, "es");

            if (blocks == null || !blocks.Any())
            {
                scheduleItems.AppendLine(
                    BusinessInfoTemplate.ScheduleItemClosed.Replace("{DAY_NAME}", dayName));
            }
            else if (blocks.Count == 1)
            {
                scheduleItems.AppendLine(
                    BusinessInfoTemplate.ScheduleItemSingle
                        .Replace("{DAY_NAME}", dayName)
                        .Replace("{OPEN_TIME}", blocks[0].Open)
                        .Replace("{CLOSE_TIME}", blocks[0].Close));
            }
            else
            {
                var timeBlocks = string.Join(" y ", blocks.Select(b => $"{b.Open} - {b.Close}"));
                scheduleItems.AppendLine(
                    BusinessInfoTemplate.ScheduleItemMultiple
                        .Replace("{DAY_NAME}", dayName)
                        .Replace("{TIME_BLOCKS}", timeBlocks));
            }
        }

        return BusinessInfoTemplate.ScheduleSection
            .Replace("{SCHEDULE_ITEMS}", scheduleItems.ToString().TrimEnd());
    }

    /// <summary>
    /// Construye la sección de métodos de pago usando el template.
    /// </summary>
    private string BuildPaymentMethodsSection(List<PaymentMethod> paymentMethods)
    {
        if (!paymentMethods.Any())
            return string.Empty;

        var paymentItems = new StringBuilder();
        foreach (var method in paymentMethods)
        {
            var icon = string.IsNullOrEmpty(method.Icon) ? "•" : method.Icon;
            paymentItems.AppendLine(
                BusinessInfoTemplate.PaymentMethodItem
                    .Replace("{ICON}", icon)
                    .Replace("{METHOD_NAME}", method.Name));
        }

        return BusinessInfoTemplate.PaymentMethodsSection
            .Replace("{PAYMENT_ITEMS}", paymentItems.ToString().TrimEnd());
    }

    /// <summary>
    /// Construye la sección de constraints del sistema.
    /// Usa el template de SystemConstraints y lo rellena con datos reales.
    /// </summary>
    private string BuildSystemConstraintsSection(LoadedBusinessContext context)
    {
        // Construir lista detallada de servicios
        var servicesList = new StringBuilder();
        var activeServices = context.Services.Where(s => s.IsActive).ToList();
        
        if (!activeServices.Any())
        {
            servicesList.AppendLine("(No hay servicios configurados actualmente)");
        }
        else
        {
            foreach (var service in activeServices)
            {
                servicesList.AppendLine($"**{service.Name}**");
                
                if (!string.IsNullOrEmpty(service.Description))
                {
                    servicesList.AppendLine($"{service.Description}");
                }
                
                var metadata = new List<string>();
                if (service.DurationMinutes > 0)
                    metadata.Add($"Duración: {service.DurationMinutes} min");
                if (service.Price > 0)
                    metadata.Add($"Precio: ${service.Price:N0} COP");
                
                if (metadata.Any())
                {
                    servicesList.AppendLine($"({string.Join(" | ", metadata)})");
                }
                
                servicesList.AppendLine();
            }
        }

        // Construir resumen de horarios
        var scheduleInfo = context.Info.Schedule.Any() 
            ? "Ver sección 'Horarios de atención' arriba" 
            : "Consultar con el cliente";

        // Construir métodos de pago
        var paymentInfo = context.Info.PaymentMethods.Any()
            ? string.Join(", ", context.Info.PaymentMethods.Select(p => p.Name))
            : "Consultar";

        // Construir información de contacto
        var contactInfo = BuildContactInfo(context.Info);

        // Rellenar template
        var constraints = SystemConstraints.Template
            .Replace("{SERVICES_LIST}", servicesList.ToString().TrimEnd())
            .Replace("{BUSINESS_NAME}", context.Info.Name)
            .Replace("{BUSINESS_DESCRIPTION}", context.Info.Description ?? "No disponible")
            .Replace("{BUSINESS_SCHEDULE}", scheduleInfo)
            .Replace("{PAYMENT_METHODS}", paymentInfo)
            .Replace("{CONTACT_INFO}", contactInfo);

        return constraints;
    }

    /// <summary>
    /// Construye información de contacto formateada.
    /// </summary>
    private string BuildContactInfo(BusinessInfo info)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(info.Phone)) parts.Add($"Tel: {info.Phone}");
        if (!string.IsNullOrEmpty(info.Email)) parts.Add($"Email: {info.Email}");
        if (!string.IsNullOrEmpty(info.Website)) parts.Add($"Web: {info.Website}");
        return parts.Any() ? string.Join(" | ", parts) : "No disponible";
    }

    /// <summary>
    /// Carga el template de guía de ventas y reemplaza placeholders.
    /// Backend SOLO carga, NO genera contenido.
    /// </summary>
    private string BuildSalesGuidanceSection(SalesGuidance guidance)
    {
        // Construir sección de atributos críticos (opcional)
        var criticalAttributesSection = string.Empty;
        if (guidance.CriticalAttributes.Any())
        {
            var attributeItems = new StringBuilder();
            foreach (var attr in guidance.CriticalAttributes)
            {
                attributeItems.AppendLine(
                    SalesGuidanceTemplate.CriticalAttributeItem.Replace("{ATTRIBUTE}", attr));
            }

            criticalAttributesSection = SalesGuidanceTemplate.CriticalAttributesSection
                .Replace("{CRITICAL_ATTRIBUTES_ITEMS}", attributeItems.ToString().TrimEnd());
        }

        // Construir sección de pregunta ejemplo (opcional)
        var exampleQuestionSection = !string.IsNullOrEmpty(guidance.ExampleQuestion)
            ? SalesGuidanceTemplate.ExampleQuestionSection.Replace("{EXAMPLE_QUESTION}", guidance.ExampleQuestion)
            : string.Empty;

        // CARGAR template y REEMPLAZAR placeholders
        return SalesGuidanceTemplate.Template
            .Replace("{GUIDANCE_TEXT}", guidance.GuidanceText)
            .Replace("{CRITICAL_ATTRIBUTES_SECTION}", criticalAttributesSection)
            .Replace("{EXAMPLE_QUESTION_SECTION}", exampleQuestionSection)
            .Replace("\n\n\n", "\n\n") // Limpiar líneas vacías múltiples
            .Trim();
    }

    /// <summary>
    /// Obtiene el orden numérico de un día usando DayOfWeek enum.
    /// Simple y robusto: 1 línea en vez de 14.
    /// </summary>
    private int GetDayOfWeekOrder(string day)
    {
        // Parsear a DayOfWeek y ajustar para que Monday sea 1 (en vez de Sunday=0)
        if (Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var dayOfWeek))
        {
            return dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
        }
        return 999; // Fallback para días no reconocidos
    }
}
