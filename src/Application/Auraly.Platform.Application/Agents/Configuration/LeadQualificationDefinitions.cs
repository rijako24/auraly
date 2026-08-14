namespace Auraly.Platform.Application.Agents.Configuration;

public sealed class LeadQualificationStageDefinition
{
    public string Band { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string? Label { get; init; }
    public bool ConversionOnRequestCompleted { get; init; }
}
