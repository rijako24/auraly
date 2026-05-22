using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Configuration;
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
    public BookingPolicyParams? BookingPolicy { get; init; }
    public PaymentTransaction? LatestPayment { get; init; }
    public EngagementContext Engagement { get; init; } = EngagementContext.ContinuingSession;
    public IReadOnlyList<IAgentTool> EnabledTools { get; init; } = [];
}
