using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proyección de servicio para la capa de aplicación.
///
/// Category + Tier permiten al ServiceCatalogBuilder agrupar y ordenar servicios
/// de mayor a menor nivel para que el LLM recomiende primero la opción más completa.
/// </summary>
public class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Categoría del servicio (Plan, Taller, Clase, Otro).
    /// </summary>
    public ServiceCategory Category { get; set; } = ServiceCategory.Otro;

    /// <summary>
    /// Orden de recomendación dentro de la categoría (Deluxe > Premium > Base).
    /// </summary>
    public ServiceTier Tier { get; set; } = ServiceTier.Base;

    /// <summary>
    /// Tipo de servicio: Standard (principal) o AddOn (extra opcional).
    /// </summary>
    public ServiceType ServiceType { get; set; } = ServiceType.Standard;

    /// <summary>
    /// Componentes que forman este bundle, ordenados por DisplayOrder.
    /// Lista vacía si el servicio no es un bundle.
    /// </summary>
    public List<BundleItemInfo> BundleItems { get; set; } = new();

    /// <summary>
    /// Verdadero cuando el servicio está compuesto por otros (tiene BundleItems).
    /// </summary>
    public bool IsBundle => BundleItems.Count > 0;

    public Dictionary<string, string> Metadata { get; set; } = new();
}
