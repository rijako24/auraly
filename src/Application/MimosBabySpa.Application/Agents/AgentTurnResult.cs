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

    public static AgentTurnResult Ok(
        string response,
        bool escalated = false,
        bool requestCompleted = false,
        int tokens = 0,
        int toolCalls = 0,
        IReadOnlyList<OutboundMessage>? outboundMessages = null) =>
        new()
        {
            Success = true,
            Response = response,
            EscalatedToHuman = escalated,
            RequestCompleted = requestCompleted,
            TotalTokens = tokens,
            ToolCallCount = toolCalls,
            OutboundMessages = outboundMessages ?? []
        };

    public static AgentTurnResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}