using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record TurnPlanFlowOption(
    string Id,
    string Type,
    string RoutingGuidance);

public sealed record TurnPlanStageOption(
    string FlowId,
    string StageId,
    string Goal,
    string? ConversationGuidance,
    IReadOnlyList<string> AdvanceWhenFacts,
    IReadOnlyList<string> Collect,
    IReadOnlyList<string> Signals,
    bool IsCurrent);

public sealed record TurnPlanScope(
    IReadOnlyDictionary<string, FactSchemaEntry> Facts,
    IReadOnlyDictionary<string, StageSignalDefinition> Signals)
{
    public IReadOnlyDictionary<string, TurnPlanFlowOption> Flows { get; init; }
        = new Dictionary<string, TurnPlanFlowOption>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<TurnPlanStageOption> Stages { get; init; } = [];
    public string PrimaryFlowId { get; init; } = string.Empty;
    public string? ActiveFlowId { get; init; }
}

public static class TurnPlanScopeBuilder
{
    public static TurnPlanScope Build(
        AgentConfig config,
        AgentFlowStage currentStage,
        IReadOnlyDictionary<string, string> currentFacts,
        string? activeFlowId = null)
    {
        var flows = AgentFlowCatalog.EffectiveFlows(config)
            .Where(flow => !string.IsNullOrWhiteSpace(flow.Id))
            .ToList();
        var currentFlow = flows.FirstOrDefault(flow => flow.Stages.Contains(currentStage))
            ?? flows.FirstOrDefault(flow => flow.Stages.Any(stage =>
                stage.Id.Equals(currentStage.Id, StringComparison.OrdinalIgnoreCase)));

        var candidates = new List<(AgentFlowDefinition Flow, AgentFlowStage Stage, bool IsCurrent)>();
        if (currentFlow is not null)
            candidates.Add((currentFlow, currentStage, true));
        foreach (var flow in flows)
        {
            if (ReferenceEquals(flow, currentFlow) || flow.Stages.Count == 0)
                continue;
            candidates.Add((flow, flow.Stages[0], false));
        }
        if (candidates.Count == 0)
            candidates.Add((new AgentFlowDefinition { Id = string.Empty }, currentStage, true));

        var eligibleFactKeys = candidates
            .SelectMany(candidate => candidate.Stage.AdvanceWhenFacts.Concat(candidate.Stage.Collect))
            .Concat(currentFacts.Keys)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var facts = config.FactSchema
            .Where(entry => eligibleFactKeys.Contains(entry.Key))
            .Where(IsPlannerWritableFact)
            .ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);

        var signals = new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in candidates.SelectMany(candidate => candidate.Stage.Signals))
        {
            if (!string.IsNullOrWhiteSpace(signal.Type))
                signals.TryAdd(signal.Type, signal);
        }

        var flowOptions = flows.ToDictionary(
            flow => flow.Id,
            flow => new TurnPlanFlowOption(flow.Id, flow.Type, flow.RoutingGuidance),
            StringComparer.OrdinalIgnoreCase);
        var stageOptions = candidates.Select(candidate => new TurnPlanStageOption(
            candidate.Flow.Id,
            candidate.Stage.Id,
            candidate.Stage.Goal,
            candidate.Stage.ConversationGuidance,
            candidate.Stage.AdvanceWhenFacts,
            candidate.Stage.Collect,
            candidate.Stage.Signals.Select(signal => signal.Type).ToList(),
            candidate.IsCurrent)).ToList();

        return new TurnPlanScope(facts, signals)
        {
            Flows = flowOptions,
            Stages = stageOptions,
            PrimaryFlowId = AgentFlowCatalog.ResolvePrimaryFlowId(config),
            ActiveFlowId = activeFlowId
        };
    }

    private static bool IsPlannerWritableFact(FactSchemaEntry entry)
    {
        if (!entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            return false;

        if (entry.ValueSource is not null
            && (entry.ValueSource.Equals("catalog", StringComparison.OrdinalIgnoreCase)
                || entry.ValueSource.Equals("tool", StringComparison.OrdinalIgnoreCase)
                || entry.ValueSource.Equals("external", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !string.Equals(entry.Role, "booking.service", StringComparison.OrdinalIgnoreCase)
            && !entry.Key.Equals(ConversationFactKeys.Service, StringComparison.OrdinalIgnoreCase);
    }
}