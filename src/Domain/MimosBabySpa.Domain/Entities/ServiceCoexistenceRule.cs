namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Regla explícita de coexistencia entre dos servicios
/// Si dos servicios tienen una regla aquí, pueden coexistir en el mismo horario
/// (siempre que haya recursos suficientes)
/// </summary>
public class ServiceCoexistenceRule
{
    public Guid ServiceCoexistenceRuleId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId1 { get; set; } // Primer servicio
    public Guid ServiceId2 { get; set; } // Segundo servicio
    public bool CanCoexist { get; set; } = true; // Por defecto true, pero puede ser false para prohibiciones explícitas
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Service Service1 { get; set; } = null!;
    public virtual Service Service2 { get; set; } = null!;
}
