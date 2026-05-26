using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resuelve el teléfono de contacto para operaciones transaccionales.
/// Prioridad: fact customer.phone → teléfono del canal.
/// </summary>
public static class ConversationContactPhone
{
    public static string? Resolve(
        IReadOnlyDictionary<string, string> facts,
        string channelPhone,
        IReadOnlyList<FactSchemaEntry>? factSchema = null)
    {
        if (factSchema is { Count: > 0 })
        {
            var index = new FactRoleIndex(factSchema);
            var fromRole = index.GetByRole(facts, FactRoles.CustomerPhone);
            if (!string.IsNullOrWhiteSpace(fromRole))
                return fromRole;
        }

        if (!string.IsNullOrWhiteSpace(channelPhone))
            return channelPhone.Trim();

        return null;
    }

    public static string? Resolve(AgentToolContext ctx) =>
        Resolve(ctx.Facts, ctx.ChannelPhone, ctx.Config?.FactSchema);
}
