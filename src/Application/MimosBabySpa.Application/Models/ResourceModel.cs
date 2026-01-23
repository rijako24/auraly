namespace MimosBabySpa.Application.Models;

/// <summary>
/// Modelo de recursos disponibles en el negocio y su uso por servicio.
/// El backend usa esto para calcular disponibilidad de forma determinística.
/// </summary>
public class ResourceModel
{
    /// <summary>
    /// Recursos disponibles y su cantidad
    /// </summary>
    public Dictionary<string, int> AvailableResources { get; set; } = new();

    /// <summary>
    /// Uso de recursos por servicio
    /// </summary>
    public Dictionary<string, ResourceUsage> ServiceResourceUsage { get; set; } = new();

    /// <summary>
    /// Reglas de coexistencia: servicios que pueden coexistir
    /// </summary>
    public List<CoexistenceRule> CoexistenceRules { get; set; } = new();
}

/// <summary>
/// Uso de recursos por un servicio específico
/// </summary>
public class ResourceUsage
{
    public Dictionary<string, int> Resources { get; set; } = new();
}

/// <summary>
/// Regla de coexistencia entre servicios
/// </summary>
public class CoexistenceRule
{
    /// <summary>
    /// Servicios que pueden coexistir (si uno está en la lista, puede coexistir con el otro)
    /// </summary>
    public List<string> Services { get; set; } = new();
}
