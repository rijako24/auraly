using MimosBabySpa.Application.Tools;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Represents one recorded tool call during a conversation turn.
/// </summary>
public record ToolCallRecord(
    ToolType ToolType,
    Dictionary<string, object> Arguments,
    ToolExecutionResult Result,
    DateTimeOffset CalledAt,
    long ElapsedMs);
