namespace MimosBabySpa.Application.Time;

/// <summary>
/// Resuelve IDs IANA/Windows de zona horaria de forma cross-platform.
/// En Windows sin datos IANA, convierte America/Bogota → SA Pacific Standard Time.
/// </summary>
public static class BusinessTimeZoneResolver
{
    public const string DefaultIanaTimeZoneId = "America/Bogota";
    private const string DefaultWindowsTimeZoneId = "SA Pacific Standard Time";

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        foreach (var candidate in EnumerateCandidates(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            DefaultIanaTimeZoneId,
            TimeSpan.FromHours(-5),
            "Bogota Standard Time",
            "Bogota Standard Time");
    }

    private static IEnumerable<string> EnumerateCandidates(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            yield return timeZoneId;

            if (OperatingSystem.IsWindows() &&
                TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                yield return windowsId;
            }

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
            {
                yield return ianaId;
            }
        }

        yield return DefaultIanaTimeZoneId;
        yield return DefaultWindowsTimeZoneId;
    }
}
