using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class StageSignalDefinition
{
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonElement ValueSchema { get; init; }
    public IReadOnlyList<SignalAmbiguityRuleDefinition> AmbiguityRules { get; init; } = [];
}

public sealed class SignalAmbiguityRuleDefinition
{
    public string Type { get; init; } = "distinct_values";
    public string ValueProperty { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public int MinimumDistinctValues { get; init; } = 2;
}
public static class StageActionTriggers
{
    public const string OnEnter = "on_enter";
    public const string WhenReady = "when_ready";
    public const string OnSignal = "on_signal";
    public const string OnFactChanged = "on_fact_changed";
    public const string Manual = "manual";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        OnEnter, WhenReady, OnSignal, OnFactChanged, Manual
    };
}

public sealed class StageActionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Trigger { get; init; } = StageActionTriggers.WhenReady;
    public string? Signal { get; init; }
    public StageConditionDefinition? Condition { get; init; }
    public Dictionary<string, JsonElement> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public StageActionExecutionDefinition Execution { get; init; } = new();
    public Dictionary<string, StageOutcomeHandlerDefinition> OnOutcome { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StageActionExecutionDefinition
{
    public string Idempotency { get; init; } = "input_version";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxAttempts { get; init; } = 1;
}

public static class StageActionIdempotency
{
    public const string InputVersion = "input_version";
    public const string OncePerRequest = "once_per_request";
    public const string None = "none";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        InputVersion, OncePerRequest, None
    };
}

public sealed class StageConditionDefinition
{
    public IReadOnlyList<StageConditionDefinition> All { get; init; } = [];
    public IReadOnlyList<StageConditionDefinition> Any { get; init; } = [];
    public StageConditionDefinition? Not { get; init; }
    public string? FactPresent { get; init; }
    public string? FactMissing { get; init; }
    public string? FactChanged { get; init; }
    public string? SignalPresent { get; init; }
    public string? VerificationActive { get; init; }
    public string? VerificationMissing { get; init; }
    public StageFactEqualityCondition? FactEquals { get; init; }
}

public sealed class StageFactEqualityCondition
{
    public string Key { get; init; } = string.Empty;
    public JsonElement Value { get; init; }
}

public sealed class StageOutcomeHandlerDefinition
{
    public IReadOnlyList<StageEffectDefinition> Effects { get; init; } = [];
    public StageResponseDefinition? Response { get; init; }
}

public sealed class StageEffectDefinition
{
    public string Type { get; init; } = string.Empty;
    public string? Fact { get; init; }
    public JsonElement Value { get; init; }
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Facts { get; init; } = [];
    public string? Template { get; init; }
    public string? DataPath { get; init; }
    public string? Mode { get; init; }
    public string? Priority { get; init; }
    public string? Sequence { get; init; }
    public string? Event { get; init; }
    public string? Reason { get; init; }
}

public static class StageEffectTypes
{
    public const string SetFact = "fact.set";
    public const string SetFactsFromOutcome = "facts.set_from_outcome";
    public const string ClearFacts = "facts.clear";
    public const string AddPresentation = "presentation.add";
    public const string EnqueueSequence = "sequence.enqueue";
    public const string EmitEvent = "event.emit";
    public const string EscalateHuman = "escalation.human";
    public const string CompleteRequest = "request.complete";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SetFact, SetFactsFromOutcome, ClearFacts, AddPresentation,
        EnqueueSequence, EmitEvent, EscalateHuman, CompleteRequest
    };
}

public sealed class StageTransitionDefinition
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public StageConditionDefinition Condition { get; init; } = new();
    public string To { get; init; } = string.Empty;
    public IReadOnlyList<StageEffectDefinition> Effects { get; init; } = [];
}

public sealed class StageResponseDefinition
{
    public string? Mode { get; init; }
    public string? Guidance { get; init; }
    public string? Template { get; init; }
    public string? SendMessageSequence { get; init; }
    public bool SuppressText { get; init; }
}
