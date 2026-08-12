namespace Auraly.Domain.Routes;

public static class RouteRules
{
    public static string NormalizeCode(string value)
    {
        var normalized = Required(value, "Route code", 32).ToUpperInvariant();
        if (normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Route code only accepts letters, numbers, hyphen and underscore.");
        return normalized;
    }

    public static string NormalizeName(string value) => Required(value, "Route name", 160);

    public static string? NormalizeNotes(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > 500) throw new ArgumentException("Route notes cannot exceed 500 characters.");
        return normalized;
    }

    public static IReadOnlyList<(int DayOfWeek, int RunOrder, TimeOnly? PlannedStartTime)> Schedules(
        IEnumerable<(int DayOfWeek, int RunOrder, TimeOnly? PlannedStartTime)> values)
    {
        var schedules = values.OrderBy(value => value.DayOfWeek).ToArray();
        if (schedules.Length == 0)
            throw new ArgumentException("Select at least one service day.");
        if (schedules.Any(value => value.DayOfWeek is < 1 or > 7))
            throw new ArgumentException("DayOfWeek must use ISO values from 1 to 7.");
        if (schedules.Any(value => value.RunOrder < 1))
            throw new ArgumentException("RunOrder must be greater than zero.");
        if (schedules.Select(value => value.DayOfWeek).Distinct().Count() != schedules.Length)
            throw new ArgumentException("A route cannot repeat a service day.");
        return schedules;
    }

    public static IReadOnlyList<Guid> CompleteOrder(
        IReadOnlyCollection<Guid> currentStopIds,
        IReadOnlyCollection<Guid> requestedStopIds)
    {
        if (requestedStopIds.Count != requestedStopIds.Distinct().Count())
            throw new ArgumentException("The requested stop order contains duplicates.");
        if (currentStopIds.Count != requestedStopIds.Count ||
            currentStopIds.Except(requestedStopIds).Any())
            throw new ArgumentException("The complete active stop collection is required to reorder a route.");
        return requestedStopIds.ToArray();
    }

    public static string PreparationStatus(int scheduleCount, int stopCount, bool hasConflict) =>
        hasConflict ? "AttentionRequired" : scheduleCount > 0 && stopCount > 0 ? "Ready" : "Draft";

    private static string Required(string value, string field, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{field} is required.");
        if (normalized.Length > maximum) throw new ArgumentException($"{field} cannot exceed {maximum} characters.");
        return normalized;
    }
}
