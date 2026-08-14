using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents.Facts;

/// <summary>
/// Plugin que resuelve el valor de un fact cuya source le corresponde.
/// Registrar múltiples implementaciones vía DI (IEnumerable).
/// </summary>
public interface IFactSourceResolver
{
    /// <summary>Valor de source que este resolver atiende (ej. "channel", "session").</summary>
    string SourceName { get; }

    /// <summary>
    /// Intenta resolver el valor del fact.
    /// Devuelve null si no puede resolverlo (otro resolver lo intentará).
    /// </summary>
    string? Resolve(FactSchemaEntry entry, FactHydratorContext context);
}
