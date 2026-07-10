using System.Diagnostics;
using System.Text.Json;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Decorador sobre IAgentTool que registra cada ejecucion en un ToolCallLog.
/// Usa el patron Decorator - envuelve cualquier IAgentTool sin modificarlo.
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
    public IReadOnlyList<string> Capabilities => _inner.Capabilities;
    public string Description => _inner.Description;
    public string ParametersSchema => _inner.ParametersSchema;
    public string? DefaultTemplateId => _inner.DefaultTemplateId;
    public IReadOnlyList<string> RequiredTemplateIds => _inner.RequiredTemplateIds;
    public string? DefaultTemplate => _inner.DefaultTemplate;

    public string BuildParametersSchema(AgentConfig config) =>
        _inner.BuildParametersSchema(config);

    public Func<JsonElement, AgentToolContext, IReadOnlyDictionary<string, string>?>? VerificationDependencyResolver =>
        _inner.VerificationDependencyResolver;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.ExecuteAsync(arguments, context, cancellationToken);
        sw.Stop();

        var isError = IsToolError(result);

        _log.Add(new ToolCallRecord(
            ToolName: _inner.Name,
            ArgumentsJson: arguments.GetRawText(),
            ResultJson: result,
            ResultIsError: isError,
            CalledAt: DateTimeOffset.UtcNow,
            ElapsedMs: sw.ElapsedMilliseconds,
            FactsJson: JsonSerializer.Serialize(context.Facts),
            ActivePaymentCheckoutSnapshotJson: context.ActivePayment?.CheckoutSnapshotJson,
            ActivePaymentAmountInCents: context.ActivePayment?.AmountInCents));

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
