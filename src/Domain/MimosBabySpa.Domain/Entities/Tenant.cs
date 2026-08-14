namespace MimosBabySpa.Domain.Entities;

public class Tenant
{
    public string TenantKey { get; private set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<Business> Businesses { get; set; } = new List<Business>();
    public virtual ICollection<AppUser> AppUsers { get; set; } = new List<AppUser>();
}
