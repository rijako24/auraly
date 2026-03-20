namespace MimosBabySpa.Domain.Models.Flow;

/// <summary>
/// When set on <see cref="FlowTurnContext"/>, the traversal engine stops and maps this to the public orchestrator result.
/// </summary>
public sealed class FlowTurnTerminationRequest
{
    public bool Success { get; init; }
    public string BotResponse { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public bool IsEscalated { get; init; }
    public bool IsFlowComplete { get; init; }
    public string? CurrentNodeId { get; init; }

    public static FlowTurnTerminationRequest Escalated(string response) =>
        new() { Success = true, BotResponse = response, IsEscalated = true };

    public static FlowTurnTerminationRequest Ok(string response, string? nodeId = null, bool isComplete = false) =>
        new() { Success = true, BotResponse = response, CurrentNodeId = nodeId, IsFlowComplete = isComplete };

    public static FlowTurnTerminationRequest Err(string error, string fallback) =>
        new() { Success = false, ErrorMessage = error, BotResponse = fallback };
}
