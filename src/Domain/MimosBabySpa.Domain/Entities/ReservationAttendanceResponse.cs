using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class ReservationAttendanceResponse
{
    public Guid ReservationAttendanceResponseId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid? SourceJobId { get; set; }
    public ReservationAttendanceResponseType ResponseType { get; set; }
    public DateTime RespondedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Reservation Reservation { get; set; } = null!;
    public virtual ScheduledAutomationJob? SourceJob { get; set; }
}
