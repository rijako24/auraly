using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Planning;

public static class TurnPlanOperations
{
    public const string Set = "set";
    public const string Clear = "clear";
}

public sealed class TurnPlan
{
    public PlannedFlowIntent FlowIntent { get; init; } = new();
    public IReadOnlyList<PlannedFactClaim> Facts { get; init; } = [];
    public IReadOnlyList<PlannedSignal> Signals { get; init; } = [];
    public PlannedCustomerDecision? Decision { get; init; }
    public TurnPlanResponseDirective Response { get; init; } = new();
}

public sealed class PlannedFlowIntent
{
    public string CandidateFlow { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string? Evidence { get; init; }
}

public sealed class PlannedFactClaim
{
    public string Key { get; init; } = string.Empty;
    public string Operation { get; init; } = TurnPlanOperations.Set;
    public JsonElement Value { get; init; }
    public string Evidence { get; init; } = string.Empty;
}

public sealed class PlannedSignal
{
    public string Type { get; init; } = string.Empty;
    public JsonElement Value { get; init; }
    public string Evidence { get; init; } = string.Empty;
}

public sealed class PlannedCustomerDecision
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string? ArtifactId { get; init; }
    public long? RequestRevision { get; init; }
}

public sealed class TurnPlanResponseDirective
{
    public string Mode { get; init; } = "continue";
    public IReadOnlyList<string> AmbiguousFields { get; init; } = [];
}
