using MimosBabySpa.Application.Messaging;

namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class AgentOperationalLimits
{
    public int InputMaxChars { get; init; } = 4000;
    public int OutputMaxChars { get; init; } = WhatsAppMessageLimits.MaxTextBodyChars;
    public int MaxResponseTokens { get; init; } = 800;
}
