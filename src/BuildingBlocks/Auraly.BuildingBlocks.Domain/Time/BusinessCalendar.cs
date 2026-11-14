using NodaTime;
using NodaTime.TimeZones;

namespace Auraly.BuildingBlocks.Domain.Time;

public static class BusinessCalendar
{
    // TZDB is shared across operating systems. Windows registry rules lose
    // historical date-line changes and can place a boundary one millisecond early.
    public static string NormalizeZoneId(string configuredId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredId);
        var id = configuredId.Trim();
        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is not null) return id;
        if (TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping.TryGetValue(id, out var ianaId))
            return ianaId;
        throw new ArgumentException("La zona horaria no es válida.", nameof(configuredId));
    }

    /// <summary>Inclusive local-day boundary. A skipped day has the same boundary as the next day.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var mapping = Resolve(zone).MapLocal(LocalDate.FromDateOnly(day).AtMidnight());
        return mapping.Count == 0
            ? mapping.LateInterval.Start.ToDateTimeOffset()
            : mapping.First().ToDateTimeOffset();
    }

    public static DateOnly DateAt(DateTimeOffset instant, TimeZoneInfo zone) =>
        Instant.FromDateTimeOffset(instant).InZone(Resolve(zone)).Date.ToDateOnly();

    public static DateTimeOffset InZone(DateTimeOffset instant, TimeZoneInfo zone) =>
        Instant.FromDateTimeOffset(instant).InZone(Resolve(zone)).ToDateTimeOffset();

    public static DateTimeOffset AtNoon(DateOnly day, TimeZoneInfo zone)
    {
        var mapping = Resolve(zone).MapLocal(LocalDate.FromDateOnly(day).At(new LocalTime(12, 0)));
        if (mapping.Count != 1)
            throw new ArgumentException("La fecha del pago no existe o no es única en la zona horaria de la sede.", nameof(day));
        return mapping.Single().ToDateTimeOffset();
    }

    private static DateTimeZone Resolve(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateTimeZoneProviders.Tzdb[NormalizeZoneId(zone.Id)];
    }
}
