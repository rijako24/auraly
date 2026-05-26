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

    /// <summary>
    /// Maps a slug-normalized label to its canonical key.
    /// e.g. "fecha deseada" → slug "fecha_deseada" → "desired_date"
    /// </summary>
    private readonly Dictionary<string, string> _labelSlugToKey =
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

            if (!string.IsNullOrWhiteSpace(entry.Label))
            {
                var slug = ToSlug(entry.Label);
                if (!string.IsNullOrWhiteSpace(slug))
                    _labelSlugToKey.TryAdd(slug, entry.Key);
            }
        }
    }

    /// <summary>
    /// Converts a human-readable label to a snake_case slug for lookup.
    /// "fecha deseada" → "fecha_deseada", "plan / servicio" → "plan_servicio"
    /// </summary>
    private static string ToSlug(string label)
    {
        var sb = new System.Text.StringBuilder();
        var prevUnderscore = false;
        foreach (var c in label.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                prevUnderscore = false;
            }
            else if (!prevUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                prevUnderscore = true;
            }
        }
        return sb.ToString().TrimEnd('_');
    }

    /// <summary>
    /// Resuelve el key canónico a partir de un rol semántico.
    /// Si no está definido el rol, devuelve null.
    /// </summary>
    public string? KeyByRole(string role) =>
        _roleToKey.TryGetValue(role, out var key) ? key : null;

    /// <summary>
    /// Normalizes a raw key: checks aliases first, then label slugs, then returns as-is.
    /// This corrects common LLM mistakes like "complementos" → "add_ons" or
    /// "fecha_deseada" (slug of "fecha deseada") → "desired_date".
    /// </summary>
    public string NormalizeKey(string rawKey)
    {
        var slug = ToSlug(rawKey);
        if (_labelSlugToKey.TryGetValue(slug, out var byLabel))
            return byLabel;

        return rawKey;
    }

    /// <summary>
    /// Devuelve la entrada de schema para un key (después de normalizar alias).
    /// </summary>
    public FactSchemaEntry? EntryFor(string rawKey)
    {
        var key = NormalizeKey(rawKey);
        return _schema.FirstOrDefault(e =>
            e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public string? RoleForKey(string rawKey)
    {
        var key = NormalizeKey(rawKey);
        return _schema.FirstOrDefault(e =>
            e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Role;
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
