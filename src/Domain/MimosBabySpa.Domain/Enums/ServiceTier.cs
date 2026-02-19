namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Nivel de un servicio dentro de su grupo de variantes.
/// Determina la prioridad de recomendación: mayor valor = más premium = se recomienda primero.
/// </summary>
public enum ServiceTier
{
    /// <summary>
    /// Servicio base del grupo. Se presenta como alternativa más accesible.
    /// </summary>
    Base = 0,

    /// <summary>
    /// Servicio premium del grupo. Se recomienda por defecto como "la experiencia más completa".
    /// </summary>
    Premium = 1,

    /// <summary>
    /// Servicio de máximo nivel dentro del grupo (p. ej. VIP, Deluxe).
    /// Si existe, se recomienda antes que Premium.
    /// </summary>
    Deluxe = 2
}
