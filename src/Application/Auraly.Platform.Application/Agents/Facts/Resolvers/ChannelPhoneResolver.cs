using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents.Facts.Resolvers;

/// <summary>
/// Resuelve facts con source="channel" y type="phone" desde el teléfono del canal.
/// </summary>
public sealed class ChannelPhoneResolver : IFactSourceResolver
{
    public string SourceName => "channel";

    public string? Resolve(FactSchemaEntry entry, FactHydratorContext context)
    {
        if (!entry.Type.Equals("phone", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(context.ChannelPhone)
            ? null
            : context.ChannelPhone.Trim();
    }
}
