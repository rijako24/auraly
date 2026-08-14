namespace Auraly.Platform.Application.Models;

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
