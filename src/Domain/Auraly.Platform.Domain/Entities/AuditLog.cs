namespace Auraly.Platform.Domain.Entities;

public class AuditLog
{
    public Guid AuditLogId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? BusinessId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }

    public virtual AppUser? User { get; set; }
    public virtual Tenant? Tenant { get; set; }
}
