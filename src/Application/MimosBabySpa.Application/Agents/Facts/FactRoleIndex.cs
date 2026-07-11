using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// ?ndice determin?stico de facts por rol sem?ntico y clave can?nica.
/// </summary>
public sealed class FactRoleIndex
{
    private readonly Dictionary<string, string> _roleToKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<FactSchemaEntry> _schema;

    public FactRoleIndex(IReadOnlyList<FactSchemaEntry> schema)
    {
        _schema = schema;
        Build(schema);
    }

    private void Build(IReadOnlyList<FactSchemaEntry> schema)
    {
        foreach (var entry in schema)
        {
            if (!string.IsNullOrWhiteSpace(entry.Role))
                _roleToKey.TryAdd(entry.Role, entry.Key);

        }
    }

    /// <summary>
    /// Resuelve el key canónico a partir de un rol semántico.
    /// Si no está definido el rol, devuelve null.
    /// </summary>
    public string? KeyByRole(string role) =>
        _roleToKey.TryGetValue(role, out var key) ? key : null;

    /// <summary>Devuelve la entrada declarada para una clave can?nica.</summary>
    public FactSchemaEntry? EntryFor(string rawKey)
    {
        return _schema.FirstOrDefault(e =>
            e.Key.Equals(rawKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Obtiene el valor de un fact desde el diccionario usando rol semántico.
    /// Equivalente al futuro ctx.GetFactByRole("customer.name").
    /// </summary>
    public string? GetByRole(
        IReadOnlyDictionary<string, string> facts,
        string role)
    {
        var key = KeyByRole(role);
        if (key is null) return null;
        return facts.TryGetValue(key, out var val)
            && !string.IsNullOrWhiteSpace(val) ? val.Trim() : null;
    }
}
