using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.DTOs;

public class ReservationDto
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime? ReservationDateTime { get; set; }
    public int? DurationMinutes { get; set; }
    public ReservationStatus Status { get; set; }
    public Guid? ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public DateTime? ReservationDate => ReservationDateTime?.Date;
    public TimeSpan? ReservationTime => ReservationDateTime?.TimeOfDay;
    public DateTime? EndDateTime =>
        ReservationDateTime.HasValue && DurationMinutes.HasValue
            ? ReservationDateTime.Value.AddMinutes(DurationMinutes.Value)
            : null;
}
