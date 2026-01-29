namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de guía de ventas para un negocio.
/// Define reglas y comportamientos antes de recomendar servicios.
/// </summary>
public class SalesGuidance
{
    /// <summary>
    /// Atributos críticos que deben estar presentes antes de recomendar un servicio.
    /// Ejemplo: ["BabyAge", "SpecialConditions"]
    /// </summary>
    public List<string> CriticalAttributes { get; set; } = new();

    /// <summary>
    /// Texto de guía que aparecerá en el system prompt.
    /// Ejemplo: "Antes de recomendar un plan, valida que conoces la edad del bebé."
    /// </summary>
    public string GuidanceText { get; set; } = string.Empty;

    /// <summary>
    /// Pregunta de ejemplo para obtener información crítica.
    /// Ejemplo: "Para poder recomendarte el plan ideal, ¿me cuentas cuántos meses tiene tu bebé? 😊"
    /// </summary>
    public string ExampleQuestion { get; set; } = string.Empty;

    /// <summary>
    /// Unidad de medida del atributo principal (ej: "meses", "años", etc.)
    /// </summary>
    public string? AttributeUnit { get; set; }

    /// <summary>
    /// Indica si esta configuración está activa.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Crea una instancia por defecto para negocios sin configuración específica.
    /// </summary>
    public static SalesGuidance Default()
    {
        return new SalesGuidance
        {
            CriticalAttributes = new List<string>(),
            GuidanceText = "Recomienda los servicios que mejor se adapten a las necesidades del cliente.",
            ExampleQuestion = string.Empty,
            AttributeUnit = null,
            IsEnabled = false // Por defecto deshabilitado si no hay configuración
        };
    }

    /// <summary>
    /// Crea una configuración típica para negocios orientados a bebés (como MimosBabySpa).
    /// </summary>
    public static SalesGuidance ForBabySpa()
    {
        return new SalesGuidance
        {
            CriticalAttributes = new List<string> { "BabyAge" },
            GuidanceText = @"Antes de recomendar un plan, valida que conoces la edad del bebé.
La edad es clave para elegir el servicio correcto y garantizar la seguridad del bebé.",
            ExampleQuestion = "Para poder recomendarte el plan ideal, ¿me cuentas cuántos meses tiene tu bebé? 😊",
            AttributeUnit = "meses",
            IsEnabled = true
        };
    }
}
