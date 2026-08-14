namespace Auraly.Platform.Application.Services;

public sealed record LeadQualificationUpdate(
    string Band,
    int Priority,
    string? Label,
    string FlowId,
    string StageId,
    bool Converted);
