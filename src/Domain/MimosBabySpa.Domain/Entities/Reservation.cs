using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Reservation
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty; // WhatsApp number
    public string ServiceName { get; set; } = string.Empty; // Nombre del servicio o plan
    public DateTime ReservationDate { get; set; } // Fecha de la reserva
    public TimeSpan ReservationTime { get; set; } // Hora de la reserva
    public int DurationMinutes { get; set; } // Duración del servicio en minutos
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public string? CalendarEventId { get; set; } // ID del evento en el calendario externo
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    
    // Helper properties
    public DateTime ReservationDateTime => ReservationDate.Date.Add(ReservationTime);
    public DateTime EndDateTime => ReservationDateTime.AddMinutes(DurationMinutes);
}
