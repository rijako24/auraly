using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Testing;

public sealed class AgentTestToolDecorator : IAgentTool
{
    private readonly IAgentTool _inner;
    private readonly AgentTestExecutionLog _log;

    public AgentTestToolDecorator(IAgentTool inner, AgentTestExecutionLog log)
    {
        _inner = inner;
        _log = log;
    }

    public string Name => _inner.Name;
    public IReadOnlyList<string> Capabilities => _inner.Capabilities;
    public IReadOnlyList<string> OperatingGroups => _inner.OperatingGroups;
    public string Description => _inner.Description;
    public string ParametersSchema => _inner.ParametersSchema;
    public string? DefaultTemplateId => _inner.DefaultTemplateId;
    public string? DefaultTemplate => _inner.DefaultTemplate;
    public IReadOnlyList<string> SemanticTriggers => _inner.SemanticTriggers;
    public Func<JsonElement, AgentToolContext, IReadOnlyDictionary<string, string>?>? VerificationDependencyResolver =>
        _inner.VerificationDependencyResolver;

    public string BuildParametersSchema(AgentConfig config) => _inner.BuildParametersSchema(config);

    public ToolAvailabilityResult Evaluate(AgentToolContext ctx, JsonElement arguments) =>
        _inner.Evaluate(ctx, arguments);

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var realResult = await _inner.ExecuteAsync(arguments, ctx, cancellationToken);
        _log.Add("tool_executed", Name, new { mocked = false, arguments = SafeJson(arguments) });
        return realResult;
    }

    private static object? SafeJson(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(element.GetRawText());
        }
        catch
        {
            return element.GetRawText();
        }
    }

    private static object? SafeJson(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(rawJson);
        }
        catch
        {
            return rawJson;
        }
    }
}
