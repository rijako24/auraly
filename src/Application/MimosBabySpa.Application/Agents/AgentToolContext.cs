using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Contexto de sesión inyectado a cada tool en el turno.
/// Facts y pack contexts se cargan al inicio y mutan durante el turno.
/// </summary>
public sealed class AgentToolContext
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid ConversationId { get; init; }
    public DateOnly BusinessToday { get; init; }
    public DateTimeOffset BusinessNow { get; init; }
    public string ChannelPhone { get; init; } = string.Empty;
    public IReadOnlyList<string> EscalationContacts { get; init; } = [];
    public int CurrentToolIteration { get; set; }
    public string? CurrentStageId { get; set; }

    public AgentConfig? Config { get; set; }

    public ConversationState ConversationState { get; init; } = null!;
    public Conversation Conversation { get; init; } = null!;
    public Dictionary<string, string> Facts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Último mensaje del usuario en el turno actual (para validar set_fact).</summary>
    public string? LastUserMessage { get; set; }

    private readonly Dictionary<Type, IPackContext> _packContexts = new();

    internal AgentTurnExecution? Turn { get; set; }

    public T? GetPackContext<T>() where T : class, IPackContext
    {
        if (_packContexts.TryGetValue(typeof(T), out var ctx))
            return ctx as T;

        foreach (var stored in _packContexts.Values)
        {
            if (stored is T match)
                return match;
        }

        return null;
    }

    internal void SetPackContext(IPackContext context) =>
        _packContexts[context.GetType()] = context;

    internal string? GetFactByRole(string role)
    {
        if (Config?.FactSchema is null || Config.FactSchema.Count == 0)
            return null;

        var index = new FactRoleIndex(Config.FactSchema);
        return index.GetByRole(Facts, role);
    }
}
