using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Orquestador del hidratador de facts.
/// Delega la resolución de cada fact a los IFactSourceResolver registrados.
///
/// Añadir soporte para nuevas fuentes (CRM, ERP, BI) = registrar un nuevo IFactSourceResolver en DI.
/// Sin tocar esta clase.
/// </summary>
public sealed class FactHydrator : IFactHydrator
{
    private readonly ILookup<string, IFactSourceResolver> _resolversBySource;

    public FactHydrator(IEnumerable<IFactSourceResolver> resolvers)
    {
        _resolversBySource = resolvers.ToLookup(
            r => r.SourceName,
            StringComparer.OrdinalIgnoreCase);
    }

    public void Hydrate(
        IReadOnlyList<FactSchemaEntry> factSchema,
        Dictionary<string, string> facts,
        FactHydratorContext context)
    {
        foreach (var entry in factSchema)
        {
            // Solo hidratar facts de fuente no-usuario y sin valor actual
            if (entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
                continue;

            if (facts.TryGetValue(entry.Key, out var existing)
                && !string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            var resolvers = _resolversBySource[entry.Source];
            foreach (var resolver in resolvers)
            {
                var resolved = resolver.Resolve(entry, context);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    facts[entry.Key] = resolved.Trim();
                    break;
                }
            }
        }
    }
}
