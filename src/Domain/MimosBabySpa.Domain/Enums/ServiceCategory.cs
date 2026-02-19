namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Categoría de negocio del servicio. Define el tipo de experiencia.
/// Usado para agrupación en catálogo y elegibilidad de add-ons (CompatibleServiceCategory).
/// Multitenant: cada negocio asigna categoría a sus servicios desde datos.
/// </summary>
public enum ServiceCategory
{
    /// <summary>
    /// Planes de hidroterapia/masaje: Marineritos, Aventuras, Suaves Mimos.
    /// Add-ons (Fotografía, Decoración) típicamente aplican solo a esta categoría.
    /// </summary>
    Plan = 0,

    /// <summary>
    /// Talleres grupales de estimulación (por frecuencia: 1/2/3 días, clase individual).
    /// </summary>
    Taller = 1,

    /// <summary>
    /// Clase personalizada individual (estimulación 1 a 1).
    /// </summary>
    Clase = 2,

    /// <summary>
    /// Otra categoría o no clasificado.
    /// </summary>
    Otro = 99
}
