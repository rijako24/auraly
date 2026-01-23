namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Recurso disponible en un negocio (ej: Baby Gym, Hidroterapia, Masaje)
/// </summary>
public class BusinessResource
{
    public Guid BusinessResourceId { get; set; }
    public Guid BusinessId { get; set; }
    public string ResourceName { get; set; } = string.Empty; // Ej: "Baby Gym", "Hidroterapia", "Masaje"
    public int Quantity { get; set; } // Cantidad disponible del recurso
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public virtual Business Business { get; set; } = null!;
}
