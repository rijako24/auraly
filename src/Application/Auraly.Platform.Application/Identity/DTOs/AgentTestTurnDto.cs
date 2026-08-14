using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Testing;

namespace Auraly.Platform.Application.Identity.DTOs;

public sealed class AgentTestTurnRequest
{
    public string Message { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerName { get; set; }
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<AgentTestMessageDto> History { get; set; } = [];
}

public sealed class AgentTestMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class AgentTestTurnResponse
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool EscalatedToHuman { get; set; }
    public bool RequestCompleted { get; set; }
    public int TotalTokens { get; set; }
    public int OperationCount { get; set; }
    public IReadOnlyDictionary<string, string> Facts { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<OutboundMessage> OutboundMessages { get; set; } = [];
    public IReadOnlyList<AgentTestExecutionEvent> Events { get; set; } = [];
}
