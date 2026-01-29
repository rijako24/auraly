namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Template para la sección de información del negocio.
/// Incluye descripción, horarios, métodos de pago y contacto.
/// </summary>
public static class BusinessInfoTemplate
{
    public const string Template = @"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
INFORMACIÓN DEL NEGOCIO
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**Negocio:** {BUSINESS_NAME}

{DESCRIPTION_SECTION}

{ADDRESS_SECTION}

{CONTACT_SECTION}

{SCHEDULE_SECTION}

{PAYMENT_METHODS_SECTION}
";

    // Secciones opcionales (solo se muestran si hay datos)
    
    public const string DescriptionSection = @"**Sobre nosotros:**
{DESCRIPTION}";

    public const string AddressSection = @"**Ubicación:** {ADDRESS}";

    public const string ContactSection = @"**Contacto:** {CONTACT_INFO}";

    public const string ScheduleSection = @"**Horarios de atención:**
{SCHEDULE_ITEMS}";

    public const string ScheduleItemClosed = "• {DAY_NAME}: Cerrado";
    
    public const string ScheduleItemSingle = "• {DAY_NAME}: {OPEN_TIME} - {CLOSE_TIME}";
    
    public const string ScheduleItemMultiple = "• {DAY_NAME}: {TIME_BLOCKS}";

    public const string PaymentMethodsSection = @"**Métodos de pago aceptados:**
{PAYMENT_ITEMS}";

    public const string PaymentMethodItem = "{ICON} {METHOD_NAME}";
}
