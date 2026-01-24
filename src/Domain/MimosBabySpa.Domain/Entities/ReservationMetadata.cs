namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Metadatos adicionales de una reserva almacenados como pares clave-valor.
/// Permite almacenar información específica del negocio de forma flexible.
/// </summary>
public class ReservationMetadata
{
    public Guid ReservationMetadataId { get; set; }
    public Guid ReservationId { get; set; }
    public string Field { get; set; } = string.Empty; // Clave técnica del campo (ej: "customerName", "babyAgeMonths")
    public string Value { get; set; } = string.Empty; // Valor del campo
    
    // Navigation property
    public virtual Reservation Reservation { get; set; } = null!;
}
