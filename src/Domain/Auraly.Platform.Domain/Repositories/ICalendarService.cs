namespace Auraly.Platform.Domain.Repositories;

public interface ICalendarService
{
    Task<string> CreateEventAsync(Guid businessId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default);
    Task UpdateEventAsync(Guid businessId, string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default);
    Task DeleteEventAsync(Guid businessId, string eventId, CancellationToken cancellationToken = default);
}

public class CalendarEvent
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string? Location { get; set; }
    public Dictionary<string, string>? ExtendedProperties { get; set; }
}
