namespace Auraly.Platform.Application.BusinessRules;

/// <summary>
/// Resultado de validación de reglas de negocio
/// </summary>
public class BusinessRuleValidationResult
{
    /// <summary>
    /// Indica si la validación pasó
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Razón de la validación (si falló)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Código de error (para manejo programático)
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Advertencias (no bloquean pero deben comunicarse)
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Información adicional del contexto
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();
}
