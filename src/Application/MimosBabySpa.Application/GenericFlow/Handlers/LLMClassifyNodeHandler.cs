using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Uses LLM to classify the user message into one of the defined possible values.
/// Routes to the edge whose portId matches the classified value.
/// Config:
///   prompt: string              — classification question
///   outputVariable: string      — internal variable to store result (e.g. "_confirm")
///   possibleValues: string[]    — classification options (must match edge portIds)
/// </summary>
public class LLMClassifyNodeHandler : INodeHandler
{
    private readonly ILLMAdapter _llm;
    private readonly ILogger<LLMClassifyNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.LLMClassify;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public LLMClassifyNodeHandler(ILLMAdapter llm, ILogger<LLMClassifyNodeHandler> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;

        var prompt = config.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
        var outputVar = config.TryGetProperty("outputVariable", out var ov) ? ov.GetString() ?? "" : "";
        var possibleValues = new List<string>();

        if (config.TryGetProperty("possibleValues", out var pvProp))
            foreach (var item in pvProp.EnumerateArray())
                if (item.GetString() is { } v) possibleValues.Add(v);

        if (possibleValues.Count == 0)
        {
            _logger.LogWarning("LLMClassify node {NodeId}: no possibleValues configured", node.Id);
            return NodeExecutionResult.Advance(null);
        }

        var systemPrompt = BuildClassifyPrompt(prompt, possibleValues);
        var request = new LLMRequest
        {
            Temperature = 0f,
            MaxTokens = ctx.FlowDefinition.EngineSettings.ClassifyMaxTokens,
            Messages =
            [
                new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                new LLMMessage { Role = LLMRole.User, Content = ctx.UserMessage }
            ]
        };

        try
        {
            var response = await _llm.SendMessageAsync(request, ct);
            if (!response.Success)
            {
                _logger.LogWarning("LLMClassify failed for node {NodeId}", node.Id);
                return NodeExecutionResult.Advance(possibleValues[^1]); // default to last option
            }

            var classified = response.Content.Trim().ToLowerInvariant()
                .Replace("\"", "").Replace("'", "").Trim();

            var matched = possibleValues.FirstOrDefault(v =>
                string.Equals(v, classified, StringComparison.OrdinalIgnoreCase))
                ?? possibleValues[^1];

            if (!string.IsNullOrEmpty(outputVar))
                ctx.State.Variables[outputVar] = matched;

            _logger.LogDebug("LLMClassify {NodeId}: classified='{Result}'", node.Id, matched);
            return NodeExecutionResult.Advance(matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLMClassify node {NodeId} threw exception", node.Id);
            return NodeExecutionResult.Advance(possibleValues[^1]);
        }
    }

    private static string BuildClassifyPrompt(string question, List<string> values)
    {
        var sb = new StringBuilder();
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine($"Responde SOLO con una de estas opciones exactas (sin explicación):");
        sb.AppendLine(string.Join(" | ", values));
        return sb.ToString();
    }
}
