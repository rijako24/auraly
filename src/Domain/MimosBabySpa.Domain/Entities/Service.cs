namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Servicio ofrecido por un negocio (ej: Marineritos, Aventuras Marinas)
/// </summary>
public class Service
{
    public Guid ServiceId { get; set; }
    public Guid BusinessId { get; set; }
    public string ServiceName { get; set; } = string.Empty; // Ej: "Marineritos", "Aventuras Marinas"
    public int DurationMinutes { get; set; } // Duración del servicio en minutos
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<ServiceResourceUsage> ResourceUsages { get; set; } = new List<ServiceResourceUsage>();
    public virtual ICollection<ServiceCoexistenceRule> CoexistenceRulesAsService1 { get; set; } = new List<ServiceCoexistenceRule>();
    public virtual ICollection<ServiceCoexistenceRule> CoexistenceRulesAsService2 { get; set; } = new List<ServiceCoexistenceRule>();
}
