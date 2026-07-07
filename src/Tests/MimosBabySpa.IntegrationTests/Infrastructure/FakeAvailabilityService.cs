using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public enum CalendarMode
{
    Available,
    NoSlots,
    ThrowError,
    ConditionalSlots
}

public class FakeAvailabilityService : IAvailabilityService
{
    private readonly CalendarMode _mode;
    private readonly List<string> _availableSlots;
    private readonly Dictionary<DateOnly, List<string>>? _conditionalSlots;
    private readonly List<DateTime> _callLog = [];

    public FakeAvailabilityService(
        CalendarMode mode = CalendarMode.Available,
        List<string>? availableSlots = null,
        Dictionary<DateOnly, List<string>>? conditionalSlots = null)
    {
        _mode = mode;
        _availableSlots = availableSlots ?? ["09:00", "11:00", "15:00"];
        _conditionalSlots = conditionalSlots;
    }

    public IReadOnlyList<DateTime> CallLog => _callLog.AsReadOnly();

    public Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        AvailabilityParams? policy = null,
        CancellationToken cancellationToken = default)
    {
        _callLog.Add(date);

        return _mode switch
        {
            CalendarMode.ThrowError => throw new Exception("Error de conexion con Google Calendar"),
            CalendarMode.NoSlots   => Task.FromResult(BuildNoSlotsResult(service, date)),
            CalendarMode.Available => Task.FromResult(BuildAvailableResult(service, date, _availableSlots)),
            CalendarMode.ConditionalSlots => Task.FromResult(BuildConditionalResult(service, date)),
            _ => Task.FromResult(BuildNoSlotsResult(service, date))
        };
    }

    private AvailabilityResult BuildAvailableResult(string service, DateTime date, List<string> slots) =>
        new()
        {
            IsAvailable         = true,
            AvailableOptions = slots.Select(s => new AvailabilityOption(s, s)).ToList(),
            ResponseMessage     = $"Hay disponibilidad. Horarios: {string.Join(", ", slots)}",
            RequestServiceName  = service,
            RequestDateString   = date.ToString("yyyy-MM-dd")
        };

    private AvailabilityResult BuildNoSlotsResult(string service, DateTime date) =>
        new()
        {
            IsAvailable        = false,
            AvailableOptions = [],
            ResponseMessage    = "No hay horarios disponibles para esa fecha.",
            RequestServiceName = service,
            RequestDateString  = date.ToString("yyyy-MM-dd")
        };

    private AvailabilityResult BuildConditionalResult(string service, DateTime date)
    {
        var dateOnly = DateOnly.FromDateTime(date);
        if (_conditionalSlots != null && _conditionalSlots.TryGetValue(dateOnly, out var slots) && slots.Count > 0)
            return BuildAvailableResult(service, date, slots);
        return BuildNoSlotsResult(service, date);
    }
}
