using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class ReservationIntegrationEvent
{
    public Guid ReservationIntegrationEventId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public IntegrationProvider Provider { get; set; }
    public IntegrationCapability Capability { get; set; }
    public string? ExternalEventId { get; set; }
    public IntegrationEventStatus Status { get; set; } = IntegrationEventStatus.Pending;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Reservation Reservation { get; set; } = null!;
    public virtual IntegrationConnection IntegrationConnection { get; set; } = null!;
}
