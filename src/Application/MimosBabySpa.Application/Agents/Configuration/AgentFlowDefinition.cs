namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class AgentFlowDefinition
{
    /// <summary>automatic | explicit (explicit reservado para futuro).</summary>
    public string StageDetection { get; init; } = "automatic";

    public IReadOnlyList<AgentFlowStage> Stages { get; init; } = [];
}
