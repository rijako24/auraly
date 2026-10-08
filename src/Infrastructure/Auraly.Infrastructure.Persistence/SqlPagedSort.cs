namespace Auraly.Infrastructure.Persistence;

internal static class SqlPagedSort
{
    public static string PrependDistinct(string order, params string[] leadingTerms)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", leadingTerms.Concat(order.Split(','))
            .Where(term => seen.Add(term.Trim().Split(' ', 2)[0])));
    }

    public static string Build(string? field, string? direction,
        IReadOnlyDictionary<string, string> allowedColumns, string defaultField,
        string defaultDirection, params string[] tieBreakers)
    {
        var key = field ?? defaultField;
        if (!allowedColumns.TryGetValue(key, out var column))
            throw new ArgumentException("La columna de ordenamiento no está permitida.", nameof(field));
        var order = direction ?? defaultDirection;
        if (order is not ("asc" or "desc"))
            throw new ArgumentException("La dirección de ordenamiento no es válida.", nameof(direction));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { column };
        var terms = new List<string> { $"{column} {order.ToUpperInvariant()}" };
        foreach (var tieBreaker in tieBreakers)
        {
            if (!seen.Add(tieBreaker))
                continue;
            terms.Add(tieBreaker);
        }
        return string.Join(",", terms);
    }
}
