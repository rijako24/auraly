namespace MimosBabySpa.Application.Agents;

public sealed class AgentTurnResult
{
    public bool Success { get; init; }
    public string Response { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public bool EscalatedToHuman { get; init; }
    public bool RequestCompleted { get; init; }
    public int TotalTokens { get; init; }
    public int OperationCount { get; init; }
    public IReadOnlyList<OutboundMessage> OutboundMessages { get; init; } = [];
    public IReadOnlyList<AgentTurnTraceEntry> Trace { get; init; } = [];

    public static AgentTurnResult Ok(
        string response,
        bool escalated = false,
        bool requestCompleted = false,
        int tokens = 0,
        int operationCount = 0,
        IReadOnlyList<OutboundMessage>? outboundMessages = null,
        IReadOnlyList<AgentTurnTraceEntry>? trace = null) =>
        new()
        {
            Success = true,
            Response = response,
            EscalatedToHuman = escalated,
            RequestCompleted = requestCompleted,
            TotalTokens = tokens,
            OperationCount = operationCount,
            OutboundMessages = outboundMessages ?? [],
            Trace = trace ?? []
        };

    public static AgentTurnResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}

public sealed class AgentTurnTraceEntry
{
    public string Kind { get; init; } = string.Empty;
    public int Iteration { get; init; }
    public string? StageId { get; init; }
    public string? Content { get; init; }
    public string? FinishReason { get; init; }
    public string? OperationId { get; init; }
    public string? OperationArgumentsJson { get; init; }
    public string? OperationOutcomeJson { get; init; }
    public IReadOnlyList<string> EnabledOperations { get; init; } = [];
    public IReadOnlyList<OperationCallTraceEntry> OperationCalls { get; init; } = [];
}

public sealed class OperationCallTraceEntry
{
    public string Id { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = string.Empty;
}
