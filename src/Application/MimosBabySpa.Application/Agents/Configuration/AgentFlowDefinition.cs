namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class AgentFlowDefinition
{
    public string Id { get; init; } = string.Empty;

    /// <summary>primary | secondary. Primary is the default center of gravity.</summary>
    public string Type { get; init; } = FlowTypes.Primary;

    /// <summary>Natural-language routing description used only by the generic flow router.</summary>
    public string RoutingGuidance { get; init; } = string.Empty;

    /// <summary>Optional active-flow retention window for secondary flows. Defaults are owned by the engine.</summary>
    public int? TtlSeconds { get; init; }

    /// <summary>automatic | explicit (explicit reservado para futuro).</summary>
    public string StageDetection { get; init; } = "automatic";

    public IReadOnlyList<AgentFlowStage> Stages { get; init; } = [];
}

public static class FlowTypes
{
    public const string Primary = "primary";
    public const string Secondary = "secondary";
}