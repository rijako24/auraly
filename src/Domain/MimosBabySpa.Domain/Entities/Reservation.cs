using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Reservation
{
    public Guid ReservationId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; } // ID del servicio (referencia a Services)
    public Guid EmployeeId { get; set; } // ID del empleado asignado (obligatorio)
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty; // WhatsApp number
    public DateTime ReservationDateTime { get; set; } // Fecha y hora de la reserva
    public int DurationMinutes { get; set; } // Duración del servicio en minutos
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public string? CalendarEventId { get; set; } // ID del evento en el calendario externo
    public Guid? ConversationId { get; set; } // FK a Conversation (nullable)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Service Service { get; set; } = null!;
    public virtual Employee Employee { get; set; } = null!;
    public virtual Conversation? Conversation { get; set; } // Navigation property a Conversation
    
    // Helper properties
    public DateTime ReservationDate => ReservationDateTime.Date;
    public TimeSpan ReservationTime => ReservationDateTime.TimeOfDay;
    public DateTime EndDateTime => ReservationDateTime.AddMinutes(DurationMinutes);
    
    /// <summary>
    /// Obtiene el nombre del servicio desde la relación Service.
    /// Lanza InvalidOperationException si Service no está cargado.
    /// </summary>
    public string GetServiceName()
    {
        return Service?.ServiceName 
            ?? throw new InvalidOperationException($"Service navigation property no está cargado para Reservation {ReservationId}");
    }
}
