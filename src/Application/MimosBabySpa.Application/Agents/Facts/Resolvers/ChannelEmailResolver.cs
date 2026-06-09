using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts.Resolvers;

/// <summary>
/// Resuelve facts con source="channel" y type="email" desde el email del canal.
/// </summary>
public sealed class ChannelEmailResolver : IFactSourceResolver
{
    public string SourceName => "channel";

    public string? Resolve(FactSchemaEntry entry, FactHydratorContext context)
    {
        if (!entry.Type.Equals("email", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(context.ConversationEmail)
            ? null
            : context.ConversationEmail.Trim();
    }
}
