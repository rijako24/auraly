using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts.Resolvers;

/// <summary>
/// Resuelve facts con source="session" desde el contexto de sesión del turno actual.
/// Hoy hidrata el engagement del cliente (firstEver / returningCustomer / continuingSession).
/// Este fact es efímero: se recalcula cada turno y no se persiste en BD.
/// </summary>
public sealed class EngagementResolver : IFactSourceResolver
{
    public string SourceName => "session";

    public string? Resolve(FactSchemaEntry entry, FactHydratorContext context)
    {
        if (!string.Equals(entry.Role, "session.engagement", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(context.EngagementKey)
            ? "continuingSession"
            : context.EngagementKey;
    }
}
