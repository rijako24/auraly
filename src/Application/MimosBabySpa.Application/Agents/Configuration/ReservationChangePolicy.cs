namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class ReservationChangePolicy
{
    private readonly HashSet<string> _automaticFields;
    private readonly HashSet<string> _escalateFields;

    private ReservationChangePolicy(IEnumerable<string> automaticFields, IEnumerable<string> escalateFields)
    {
        _automaticFields = automaticFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _escalateFields = escalateFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AutomaticChangeFields => _automaticFields
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> KnownChangeFields => _automaticFields
        .Concat(_escalateFields)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static ReservationChangePolicy From(ReservationManagementDefinitions? config) =>
        new(config?.AutomaticChangeFields ?? [], config?.EscalateChangeFields ?? []);

    public bool HasAutomaticField(IReadOnlyList<string> requestedFields) =>
        requestedFields.Any(_automaticFields.Contains);

    public IReadOnlyList<string> EscalationFieldsFor(IReadOnlyList<string> requestedFields)
    {
        if (requestedFields.Count == 0)
            return [];

        return requestedFields
            .Where(field => _escalateFields.Contains(field) || !_automaticFields.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}