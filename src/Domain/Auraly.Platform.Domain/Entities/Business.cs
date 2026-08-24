namespace Auraly.Platform.Domain.Entities;

public class Business
{
    public Guid BusinessId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    
    // Información de contacto y descripción
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    
    public string TimeZone { get; set; } = "America/Bogota";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<BusinessWhatsAppNumber> WhatsAppNumbers { get; set; } = new List<BusinessWhatsAppNumber>();
    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
