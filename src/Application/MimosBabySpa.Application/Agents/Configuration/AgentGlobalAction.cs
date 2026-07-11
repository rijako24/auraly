namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Deterministic behavior available from every stage. The extractor may only emit
/// its semantic signal; configured operations own all effects.
/// </summary>
public sealed class AgentGlobalAction
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string? ConversationGuidance { get; init; }
    public StageSignalDefinition Signal { get; init; } = new();
    public IReadOnlyList<StageActionDefinition> Actions { get; init; } = [];
    public StageResponseDefinition Response { get; init; } = new();
}
