using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class Reservation
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateTime? ReservationDateTime { get; set; }
    public int? DurationMinutes { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public Guid? ConversationId { get; set; }

    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerEmailSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }
    public string? AvailableSlotsCsv { get; set; }
    public bool CustomerConfirmed { get; set; }
    public string? CustomAttributesJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Service? Service { get; set; }
    public virtual Employee? Employee { get; set; }
    public virtual Conversation? Conversation { get; set; }
    public virtual ICollection<ReservationAddOn> AddOns { get; set; } = new List<ReservationAddOn>();

    public DateTime? EndDateTime =>
        ReservationDateTime.HasValue && DurationMinutes.HasValue
            ? ReservationDateTime.Value.AddMinutes(DurationMinutes.Value)
            : null;

    public string? GetServiceName() => Service?.ServiceName;
}


