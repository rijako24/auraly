using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Facts;

namespace Auraly.Platform.Application.Agents;

/// <summary>
/// Resolves the contact phone for transactional operations.
/// Priority with config: customer.phone role -> customer_phone fact -> channel phone.
/// </summary>
public static class ConversationContactPhone
{
    public static string? Resolve(IReadOnlyDictionary<string, string> facts, string channelPhone) =>
        Resolve(facts, channelPhone, config: null);

    public static string? Resolve(
        IReadOnlyDictionary<string, string> facts,
        string channelPhone,
        AgentConfig? config)
    {
        if (config is not null)
        {
            var roles = new FactRoleIndex(config.FactSchema);
            var fromRole = roles.GetByRole(facts, "customer.phone");
            if (!string.IsNullOrWhiteSpace(fromRole))
                return fromRole;
        }

        var fromFacts = ConversationFactKeys.Get(facts, ConversationFactKeys.CustomerPhone);
        if (!string.IsNullOrWhiteSpace(fromFacts))
            return fromFacts;

        if (!string.IsNullOrWhiteSpace(channelPhone))
            return channelPhone.Trim();

        return null;
    }
}
