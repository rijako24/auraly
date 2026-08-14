using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class OrderConnectionEvent
{
    public Guid OrderConnectionEventId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid OrderId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Commerce;
    public int Provider { get; set; }
    public int Capability { get; set; }
    public string? ExternalEventId { get; set; }
    public IntegrationEventStatus Status { get; set; } = IntegrationEventStatus.Pending;
    public string? LastError { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Order Order { get; set; } = null!;
    public virtual IntegrationConnection IntegrationConnection { get; set; } = null!;
}
