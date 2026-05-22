using MimosBabySpa.Application.Agents;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resuelve el teléfono de contacto para operaciones transaccionales.
/// Prioridad: fact customer_phone → teléfono del canal (WhatsApp/consola).
/// </summary>
public static class ConversationContactPhone
{
    public static string? Resolve(IReadOnlyDictionary<string, string> facts, string channelPhone)
    {
        var fromFacts = ConversationFactKeys.Get(facts, ConversationFactKeys.CustomerPhone);
        if (!string.IsNullOrWhiteSpace(fromFacts))
            return fromFacts;

        if (!string.IsNullOrWhiteSpace(channelPhone))
            return channelPhone.Trim();

        return null;
    }
}
