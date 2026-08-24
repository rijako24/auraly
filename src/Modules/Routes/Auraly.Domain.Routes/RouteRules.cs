namespace Auraly.Domain.Routes;

public static class RouteRules
{
    public static string NormalizeCode(string value)
    {
        var normalized = Required(value, "El código de la ruta", 32).ToUpperInvariant();
        if (normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("El código de la ruta solo admite letras, números, guion y guion bajo.");
        return normalized;
    }

    public static string NormalizeName(string value) => Required(value, "El nombre de la ruta", 160);

    public static string? NormalizeNotes(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > 500) throw new ArgumentException("Las notas de la ruta no pueden superar 500 caracteres.");
        return normalized;
    }

    public static IReadOnlyList<(int DayOfWeek, int RunOrder, TimeOnly? PlannedStartTime)> Schedules(
        IEnumerable<(int DayOfWeek, int RunOrder, TimeOnly? PlannedStartTime)> values)
    {
        var schedules = values.OrderBy(value => value.DayOfWeek).ToArray();
        if (schedules.Length == 0)
            throw new ArgumentException("Selecciona al menos un día de atención.");
        if (schedules.Any(value => value.DayOfWeek is < 1 or > 7))
            throw new ArgumentException("El día de la semana debe usar valores del 1 al 7.");
        if (schedules.Any(value => value.RunOrder < 1))
            throw new ArgumentException("El orden del recorrido debe ser mayor que cero.");
        if (schedules.Select(value => value.DayOfWeek).Distinct().Count() != schedules.Length)
            throw new ArgumentException("Una ruta no puede repetir un día de atención.");
        return schedules;
    }

    public static IReadOnlyList<Guid> CompleteOrder(
        IReadOnlyCollection<Guid> currentStopIds,
        IReadOnlyCollection<Guid> requestedStopIds)
    {
        if (requestedStopIds.Count != requestedStopIds.Distinct().Count())
            throw new ArgumentException("El orden solicitado de paradas contiene duplicados.");
        if (currentStopIds.Count != requestedStopIds.Count ||
            currentStopIds.Except(requestedStopIds).Any())
            throw new ArgumentException("Para reordenar la ruta se requiere la lista completa de establecimientos activos.");
        return requestedStopIds.ToArray();
    }

    public static string PreparationStatus(int scheduleCount, int stopCount, bool hasConflict) =>
        hasConflict ? "AttentionRequired" : scheduleCount > 0 && stopCount > 0 ? "Ready" : "Draft";

    private static string Required(string value, string field, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{field} es obligatorio.");
        if (normalized.Length > maximum) throw new ArgumentException($"{field} no puede superar {maximum} caracteres.");
        return normalized;
    }
}
