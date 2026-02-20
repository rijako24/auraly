using System.Diagnostics;
using Azure.AI.OpenAI;
using MimosBabySpa.Application.Tools;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Decorator over IToolHandler that logs every execution to a ToolCallLog.
/// Uses the Decorator pattern — wraps any IToolHandler without modifying it.
/// </summary>
public class ToolCallInterceptor : IToolHandler
{
    private readonly IToolHandler _inner;
    private readonly ToolType _toolType;
    private readonly ToolCallLog _log;

    public ToolCallInterceptor(IToolHandler inner, ToolType toolType, ToolCallLog log)
    {
        _inner    = inner;
        _toolType = toolType;
        _log      = log;
    }

    // ── IToolHandler contract ──────────────────────────────────────────────
    public string FunctionName => _inner.FunctionName;

    public FunctionDefinition GetDefinition() => _inner.GetDefinition();

    public async Task<ToolExecutionResult> ExecuteAsync(
        Dictionary<string, object> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.ExecuteAsync(arguments, context, cancellationToken);
        sw.Stop();

        _log.Add(new ToolCallRecord(
            ToolType:    _toolType,
            Arguments:   new Dictionary<string, object>(arguments),
            Result:      result,
            CalledAt:    DateTimeOffset.UtcNow,
            ElapsedMs:   sw.ElapsedMilliseconds));

        return result;
    }
}
