namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Uso de recursos por un servicio específico
/// Ej: Marineritos usa 1 Baby Gym + 1 Hidroterapia + 1 Masaje
/// </summary>
public class ServiceResourceUsage
{
    public Guid ServiceResourceUsageId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid BusinessResourceId { get; set; }
    public int Quantity { get; set; } // Cantidad del recurso que usa este servicio
    
    // Navigation properties
    public virtual Service Service { get; set; } = null!;
    public virtual BusinessResource BusinessResource { get; set; } = null!;
}
