using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Checks if specified fields are filled. If any are missing, generates a
/// conversational LLM response to collect them. When all are present, advances.
/// Config:
///   fields: string[] | ["*"]  — fields to collect, or all required fields
///   instructions: string?     — additional LLM instructions
///   knowledgeSourceIds: string[] — sources to include in prompt
/// </summary>
public class CollectFieldsNodeHandler : INodeHandler
{
    private readonly ILLMAdapter _llm;
    private readonly FlowPromptBuilder _promptBuilder;
    private readonly TemplateResolver _templateResolver;
    private readonly IKnowledgeSourceRepository _ksRepo;
    private readonly ILogger<CollectFieldsNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.CollectFields;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public CollectFieldsNodeHandler(
        ILLMAdapter llm,
        FlowPromptBuilder promptBuilder,
        TemplateResolver templateResolver,
        IKnowledgeSourceRepository ksRepo,
        ILogger<CollectFieldsNodeHandler> logger)
    {
        _llm = llm;
        _promptBuilder = promptBuilder;
        _templateResolver = templateResolver;
        _ksRepo = ksRepo;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;
        var flow = ctx.FlowDefinition;
        var state = ctx.State;

        var fieldKeys = GetFieldKeys(config, flow);
        var missingFields = fieldKeys
            .Where(k => state.GetVariable(k) == null)
            .Select(k => flow.Variables.FirstOrDefault(v => v.Key == k))
            .Where(v => v != null)
            .Cast<FlowVariable>()
            .ToList();

        if (missingFields.Count == 0)
        {
            _logger.LogDebug("CollectFields {NodeId}: all fields present — advancing", node.Id);
            return NodeExecutionResult.Advance();
        }

        _logger.LogDebug("CollectFields {NodeId}: missing [{Fields}]",
            node.Id, string.Join(", ", missingFields.Select(f => f.Key)));

        var nodeSources = await LoadNodeKnowledgeSources(config, ct);
        var systemPrompt = _promptBuilder.Build(ctx.Agent, node, ctx, nodeSources);

        var userPrompt = BuildUserPrompt(missingFields, ctx);

        var request = new LLMRequest
        {
            Temperature = ctx.FlowDefinition.EngineSettings.ResponseTemperature,
            MaxTokens = ctx.FlowDefinition.EngineSettings.ResponseMaxTokens,
            Messages = BuildMessages(ctx, systemPrompt, userPrompt)
        };

        var response = await _llm.SendMessageAsync(request, ct);
        if (!response.Success)
        {
            _logger.LogWarning("CollectFields LLM failed for node {NodeId}", node.Id);
            return NodeExecutionResult.WaitForUser("Disculpa, ocurrió un error. ¿Puedes repetirlo?");
        }

        return NodeExecutionResult.WaitForUser(response.Content);
    }

    private static List<string> GetFieldKeys(JsonElement config, FlowDefinitionDocument flow)
    {
        if (!config.TryGetProperty("fields", out var fieldsProp))
            return new List<string>();

        if (fieldsProp.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in fieldsProp.EnumerateArray())
            {
                var val = item.GetString();
                if (val == "*")
                    return flow.Variables
                        .Where(v => v.Required && !v.IsSystemManaged)
                        .OrderBy(v => v.DisplayOrder)
                        .Select(v => v.Key)
                        .ToList();
                if (val != null) list.Add(val);
            }
            return list;
        }

        return new List<string>();
    }

    private static string BuildUserPrompt(List<FlowVariable> missingFields, FlowTurnContext ctx)
    {
        var sb = new StringBuilder();

        if (ctx.ValidationErrors.Count > 0)
        {
            sb.AppendLine("Errores de validación del turno anterior:");
            foreach (var err in ctx.ValidationErrors)
                sb.AppendLine($"- {err.VariableKey}: \"{err.ProvidedValue}\" — {err.ErrorMessage}");
            sb.AppendLine();
        }

        sb.AppendLine($"Faltan los siguientes campos: {string.Join(", ", missingFields.Select(f => f.Label))}.");
        sb.Append("Pide la información de forma conversacional.");
        return sb.ToString();
    }

    private static List<LLMMessage> BuildMessages(
        FlowTurnContext ctx, string systemPrompt, string userPrompt)
    {
        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = systemPrompt }
        };

        foreach (var (role, content) in ctx.ConversationHistory)
        {
            if (Enum.TryParse<LLMRole>(role, ignoreCase: true, out var llmRole))
                messages.Add(new() { Role = llmRole, Content = content });
        }

        messages.Add(new() { Role = LLMRole.User, Content = ctx.UserMessage });
        messages.Add(new() { Role = LLMRole.System, Content = userPrompt });

        return messages;
    }

    private async Task<IEnumerable<KnowledgeSource>> LoadNodeKnowledgeSources(
        JsonElement config, CancellationToken ct)
    {
        if (!config.TryGetProperty("knowledgeSourceIds", out var idsProp)) return [];
        var ids = new List<Guid>();
        foreach (var item in idsProp.EnumerateArray())
        {
            if (Guid.TryParse(item.GetString(), out var id))
                ids.Add(id);
        }
        return ids.Count == 0 ? [] : await _ksRepo.GetByIdsAsync(ids, ct);
    }
}
