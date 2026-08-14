namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Define la composición de un servicio bundle.
/// Un bundle es un servicio que agrupa otros servicios incluidos en su precio.
///
/// Ejemplo: "Cumplemes Marineritos + Deco Sencilla" (bundle) incluye:
///   - Plan Marineritos (servicio base)
///   - Decoración Sencilla (add-on)
///
/// El booking se realiza sobre el bundle como un solo servicio.
/// Esta tabla responde a "¿de qué está compuesto este bundle?"
/// permitiendo al catálogo mostrar la composición real sin inferencia del LLM.
///
/// Relación complementaria con Category/Tier:
///   - Category/Tier → "¿cuál variante recomendar primero?"
///   - ServiceBundleItem → "¿de qué está hecho este servicio?"
/// </summary>
public class ServiceBundleItem
{
    public Guid ServiceBundleItemId { get; set; }

    /// <summary>
    /// El servicio bundle (el que se reserva y contiene todo).
    /// </summary>
    public Guid BundleServiceId { get; set; }

    /// <summary>
    /// El servicio incluido dentro del bundle.
    /// </summary>
    public Guid IncludedServiceId { get; set; }

    /// <summary>
    /// Orden de presentación dentro del bundle (1 = base, 2, 3... = extras).
    /// </summary>
    public int DisplayOrder { get; set; } = 1;

    // Navigation properties
    public virtual Service BundleService { get; set; } = null!;
    public virtual Service IncludedService { get; set; } = null!;
}
