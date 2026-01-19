using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class BusinessConfiguration
{
    public Guid BusinessConfigurationId { get; set; } // Guid como ID
    public Guid BusinessId { get; set; }
    public BusinessConfigurationKey Key { get; set; } // Enum como propiedad separada
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
}
