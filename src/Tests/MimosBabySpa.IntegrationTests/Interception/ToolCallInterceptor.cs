using System.Diagnostics;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Decorador sobre IAgentTool que registra cada ejecución en un ToolCallLog.
/// Usa el patrón Decorator — envuelve cualquier IAgentTool sin modificarlo.
/// </summary>
public class ToolCallInterceptor : IAgentTool
{
    private readonly IAgentTool _inner;
    private readonly ToolCallLog _log;

    public ToolCallInterceptor(IAgentTool inner, ToolCallLog log)
    {
        _inner = inner;
        _log = log;
    }

    public string Name => _inner.Name;
    public string Description => _inner.Description;
    public string ParametersSchema => _inner.ParametersSchema;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.ExecuteAsync(invocation, cancellationToken);
        sw.Stop();

        var isError = IsToolError(result);

        _log.Add(new ToolCallRecord(
            ToolName: _inner.Name,
            ArgumentsJson: invocation.Arguments.GetRawText(),
            ResultJson: result,
            ResultIsError: isError,
            CalledAt: DateTimeOffset.UtcNow,
            ElapsedMs: sw.ElapsedMilliseconds));

        return result;
    }

    private static bool IsToolError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() == false;
        }
        catch { return false; }
    }
}
