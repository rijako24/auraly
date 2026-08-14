using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class SystemConfiguration
{
    // El ID es el enum directamente
    public SystemConfigurationKey SystemConfigurationId { get; set; } // Enum como ID
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
