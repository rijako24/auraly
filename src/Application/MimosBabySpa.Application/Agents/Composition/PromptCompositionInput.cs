using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed class PromptCompositionInput
{
    public required AgentConfig Config { get; init; }
    public required IEnumerable<Message> History { get; init; }
    public required TemporalReferenceContext Temporal { get; init; }
    public AgentToolContext? Session { get; init; }
    public PaymentTransaction? LatestPayment { get; init; }

    /// <summary>
    /// El engagement se lee desde Session.Facts["session.engagement"] durante la composición.
    /// Valores: "firstEver" | "returningCustomer" | "continuingSession"
    /// </summary>
    public IReadOnlyList<IAgentTool> EnabledTools { get; init; } = [];
}
