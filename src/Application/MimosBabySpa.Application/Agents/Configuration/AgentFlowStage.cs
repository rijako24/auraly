namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>A compiled deterministic stage. The LLM may only populate its facts and signals.</summary>
public sealed class AgentFlowStage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;

    /// <summary>Optional facts accepted when the customer volunteers them early.</summary>
    public IReadOnlyList<string> Collect { get; init; } = [];

    /// <summary>Facts whose presence enables configured transitions; this list never decides what to ask.</summary>
    public IReadOnlyList<string> AdvanceWhenFacts { get; init; } = [];

    public IReadOnlyList<StageSignalDefinition> Signals { get; init; } = [];
    public IReadOnlyList<StageActionDefinition> Actions { get; init; } = [];
    public IReadOnlyList<StageTransitionDefinition> Transitions { get; init; } = [];

    /// <summary>Optional commercial classification observed by the lead projection.</summary>
    public LeadQualificationStageDefinition? LeadQualification { get; init; }

    /// <summary>
    /// Declares that every customer-visible response delivered while this stage
    /// remains active leaves the conversation waiting for the customer.
    /// </summary>
    public bool AwaitCustomerReply { get; init; }

    public StageResponseDefinition Response { get; init; } = new();

    /// <summary>Renderer guidance only; it cannot authorize operations or mutate state.</summary>
    public string? ConversationGuidance { get; init; }

    /// <summary>
    /// Dependencies that move the durable cursor back to this stage when their value changes.
    /// Fact invalidation remains derived from factSchema.dependsOn.
    /// </summary>
    public IReadOnlyList<string> ReentryOnFactChanged { get; init; } = [];
}
