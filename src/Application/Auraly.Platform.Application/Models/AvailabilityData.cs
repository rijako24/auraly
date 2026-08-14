namespace Auraly.Platform.Application.Models;

public class AvailabilityData
{
    public DateTime Date { get; set; }
    public TimeSpan Time { get; set; }
    public int? DurationMinutes { get; set; }
}
