namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Template para la sección de guía de ventas específica del negocio.
/// Solo se incluye si está habilitada en la configuración.
/// </summary>
public static class SalesGuidanceTemplate
{
    public const string Template = @"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
GUÍA DE RECOMENDACIÓN ESPECÍFICA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

{GUIDANCE_TEXT}

{CRITICAL_ATTRIBUTES_SECTION}

{EXAMPLE_QUESTION_SECTION}
";

    public const string CriticalAttributesSection = @"**Información crítica a validar antes de recomendar:**
{CRITICAL_ATTRIBUTES_ITEMS}";

    public const string CriticalAttributeItem = "• {ATTRIBUTE}";

    public const string ExampleQuestionSection = @"**Ejemplo de pregunta estratégica:**
""{EXAMPLE_QUESTION}""";
}
