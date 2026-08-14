using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents;

public sealed record LeadQualificationSnapshot(
    string Band,
    int Priority,
    string? Label,
    string FlowId,
    string StageId,
    bool Converted);

/// <summary>
/// Resolves commercial metadata already declared by the active flow. It is a
/// read-only projection: qualification never participates in routing.
/// </summary>
public static class LeadQualificationResolver
{
    public static LeadQualificationSnapshot? Resolve(
        AgentConfig config,
        string? flowId,
        string? stageId,
        bool requestCompleted)
    {
        if (string.IsNullOrWhiteSpace(flowId) || string.IsNullOrWhiteSpace(stageId))
            return null;

        var flow = AgentFlowCatalog.Find(config, flowId);
        var stage = flow?.Stages.FirstOrDefault(candidate =>
            candidate.Id.Equals(stageId, StringComparison.OrdinalIgnoreCase));
        var definition = stage?.LeadQualification;
        if (definition is null)
            return null;

        return new LeadQualificationSnapshot(
            definition.Band.Trim(),
            definition.Priority,
            definition.Label?.Trim(),
            flow!.Id,
            stage!.Id,
            definition.ConversionOnRequestCompleted && requestCompleted);
    }
}
