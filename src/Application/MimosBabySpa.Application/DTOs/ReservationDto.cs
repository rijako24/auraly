using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.DTOs;

public class ReservationDto
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public TimeSpan ReservationTime { get; set; }
    public int DurationMinutes { get; set; }
    public ReservationStatus Status { get; set; }
    public string? CalendarEventId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime ReservationDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
}
