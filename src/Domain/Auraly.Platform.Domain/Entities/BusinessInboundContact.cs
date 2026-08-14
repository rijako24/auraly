namespace Auraly.Platform.Domain.Entities;

public class BusinessInboundContact
{
    public Guid BusinessInboundContactId { get; set; }
    public Guid BusinessId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneNormalized { get; set; } = string.Empty;
    public Guid InboundAgentId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? CapabilitiesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Agent InboundAgent { get; set; } = null!;
    public virtual Employee? Employee { get; set; }
}
