using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.DTOs;

public class ReservationDto
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ServiceName { get; set; } = string.Empty; // Obtenido desde Service.ServiceName
    public string EmployeeName { get; set; } = string.Empty; // Obtenido desde Employee.Name
    public DateTime ReservationDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public ReservationStatus Status { get; set; }
    public string? CalendarEventId { get; set; }
    public Guid? ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Helper properties para compatibilidad
    public DateTime ReservationDate => ReservationDateTime.Date;
    public TimeSpan ReservationTime => ReservationDateTime.TimeOfDay;
    public DateTime EndDateTime => ReservationDateTime.AddMinutes(DurationMinutes);
}
