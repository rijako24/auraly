namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Add-on seleccionado y asociado a una reserva.
/// Permite múltiples add-ons por reserva (ej: Fotografía + Decoración).
/// </summary>
public class ReservationAddOn
{
    public Guid ReservationAddOnId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid AddOnServiceId { get; set; }

    /// <summary>
    /// Precio al momento de la reserva (auditoría).
    /// </summary>
    public decimal PriceSnapshot { get; set; }

    // Navigation properties
    public virtual Reservation Reservation { get; set; } = null!;
    public virtual Service AddOnService { get; set; } = null!;
}
