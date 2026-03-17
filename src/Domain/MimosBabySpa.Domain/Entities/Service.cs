using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Servicio ofrecido por un negocio (ej: Marineritos, Aventuras Marinas).
///
/// Categoría y tier de recomendación:
///   - CategoryId: categoría del servicio (FK a ServiceCategories). Define agrupación y elegibilidad de add-ons.
///   - Tier: orden de recomendación dentro de la misma categoría (Deluxe > Premium > Base).
/// </summary>
public class Service
{
    public Guid ServiceId { get; set; }
    public Guid BusinessId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Categoría del servicio (Plan, Taller, Clase, Otros, etc.). Por negocio.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Orden de recomendación dentro de la categoría (Deluxe > Premium > Base).
    /// </summary>
    public ServiceTier Tier { get; set; } = ServiceTier.Base;

    /// <summary>
    /// Tipo de servicio: Standard (principal reservable) o AddOn (extra opcional).
    /// </summary>
    public ServiceType ServiceType { get; set; } = ServiceType.Standard;

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual ServiceCategory ServiceCategory { get; set; } = null!;
    public virtual ICollection<ServiceResourceUsage> ResourceUsages { get; set; } = new List<ServiceResourceUsage>();

    /// <summary>
    /// Servicios que componen este bundle (cuando este servicio ES un bundle).
    /// Vacío para servicios simples o decoraciones.
    /// </summary>
    public virtual ICollection<ServiceBundleItem> BundleItems { get; set; } = new List<ServiceBundleItem>();
}
