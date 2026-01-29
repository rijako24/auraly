namespace MimosBabySpa.Application.BusinessRules;

/// <summary>
/// Contexto de reglas de negocio para un cliente/servicio
/// </summary>
public class BusinessRuleContext
{
    /// <summary>
    /// Indica si el cliente tiene restricciones especiales
    /// </summary>
    public bool HasRestrictions { get; set; }

    /// <summary>
    /// Restricciones aplicables
    /// </summary>
    public List<string> Restrictions { get; set; } = new();

    /// <summary>
    /// Indica si el cliente tiene beneficios especiales
    /// </summary>
    public bool HasBenefits { get; set; }

    /// <summary>
    /// Beneficios aplicables
    /// </summary>
    public List<string> Benefits { get; set; } = new();

    /// <summary>
    /// Información adicional del contexto
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
