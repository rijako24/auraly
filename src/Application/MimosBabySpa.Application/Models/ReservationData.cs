namespace MimosBabySpa.Application.Models;

public class ReservationData
{
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public TimeSpan ReservationTime { get; set; }
    public int DurationMinutes { get; set; } // La duración viene del servicio, no se calcula
}
