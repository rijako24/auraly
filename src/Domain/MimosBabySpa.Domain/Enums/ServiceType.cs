namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Tipo de servicio para el catálogo y flujo de reservas.
/// </summary>
public enum ServiceType
{
    /// <summary>
    /// Servicio principal reservable. Se muestra en el catálogo principal.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Extra opcional. Solo se ofrece DESPUÉS de que el usuario elige un servicio Standard.
    /// Puede ser parte de bundles (ServiceBundleItem) o ofrecido dinámicamente (ServiceAddOnRule).
    /// </summary>
    AddOn = 1
}
