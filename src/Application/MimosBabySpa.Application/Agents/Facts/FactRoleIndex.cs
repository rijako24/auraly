using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Índice de resolución de facts por rol semántico y por alias.
///
/// Permite buscar "customer.name" → key canónico "customer_name",
/// o normalizar alias "nombre" → "customer_name" antes de persistir.
///
/// Es la puerta de entrada para migrar ConversationFactKeys hardcodeados
/// hacia configuración dinámica por tenant, sin romper código existente.
/// </summary>
public sealed class FactRoleIndex
{
    private readonly Dictionary<string, string> _roleToKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _aliasToKey =
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

            foreach (var alias in entry.Aliases)
                _aliasToKey.TryAdd(alias, entry.Key);
        }
    }

    /// <summary>
    /// Resuelve el key canónico a partir de un rol semántico.
    /// Si no está definido el rol, devuelve null.
    /// </summary>
    public string? KeyByRole(string role) =>
        _roleToKey.TryGetValue(role, out var key) ? key : null;

    /// <summary>
    /// Si el input es un alias conocido, devuelve el key canónico.
    /// Si no es alias, devuelve el input tal cual (puede ser ya el key canónico).
    /// </summary>
    public string NormalizeKey(string rawKey) =>
        _aliasToKey.TryGetValue(rawKey, out var canonical) ? canonical : rawKey;

    /// <summary>
    /// Devuelve la entrada de schema para un key (después de normalizar alias).
    /// </summary>
    public FactSchemaEntry? EntryFor(string rawKey)
    {
        var key = NormalizeKey(rawKey);
        return _schema.FirstOrDefault(e =>
            e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
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
