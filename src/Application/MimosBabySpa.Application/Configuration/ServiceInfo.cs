using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proyección de servicio para la capa de aplicación.
///
/// CategoryId + Tier permiten al ServiceCatalogBuilder agrupar y ordenar servicios
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
    /// ID de la categoría del servicio.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Nombre de la categoría (para presentación).
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// DisplayOrder de la categoría (para ordenar grupos).
    /// </summary>
    public int CategoryDisplayOrder { get; set; }

    /// <summary>
    /// Orden de recomendación dentro de la categoría (Deluxe > Premium > Base).
    /// </summary>
    public ServiceTier Tier { get; set; } = ServiceTier.Base;

    /// <summary>
    /// Tipo de servicio: Standard (principal) o AddOn (extra opcional).
    /// </summary>
    public ServiceType ServiceType { get; set; } = ServiceType.Standard;

    /// <summary>
    /// Define si el servicio se reserva por disponibilidad o se inscribe en horario fijo.
    /// </summary>
    public ServiceFulfillmentKind FulfillmentKind { get; set; } = ServiceFulfillmentKind.Reservation;

    /// <summary>
    /// Horario fijo de inscripcion para servicios Enrollment.
    /// </summary>
    public string? FixedScheduleLabel { get; set; }

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
