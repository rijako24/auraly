namespace MimosBabySpa.Application.Agents;

public sealed class AgentTurnResult
{
    public bool Success { get; init; }
    public string Response { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public bool EscalatedToHuman { get; init; }
    public bool RequestCompleted { get; init; }
    public int TotalTokens { get; init; }
    public int ToolCallCount { get; init; }
    public IReadOnlyList<OutboundMessage> OutboundMessages { get; init; } = [];
    public IReadOnlyList<AgentTurnTraceEntry> Trace { get; init; } = [];

    public static AgentTurnResult Ok(
        string response,
        bool escalated = false,
        bool requestCompleted = false,
        int tokens = 0,
        int toolCalls = 0,
        IReadOnlyList<OutboundMessage>? outboundMessages = null,
        IReadOnlyList<AgentTurnTraceEntry>? trace = null) =>
        new()
        {
            Success = true,
            Response = response,
            EscalatedToHuman = escalated,
            RequestCompleted = requestCompleted,
            TotalTokens = tokens,
            ToolCallCount = toolCalls,
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
    public string? ToolName { get; init; }
    public string? ToolArgumentsJson { get; init; }
    public string? ToolResultJson { get; init; }
    public IReadOnlyList<string> EnabledTools { get; init; } = [];
    public IReadOnlyList<ToolCallTraceEntry> ToolCalls { get; init; } = [];
}

public sealed class ToolCallTraceEntry
{
    public string Id { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = string.Empty;
}
