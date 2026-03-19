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
/// Generates a bot response using LLM or a static template.
/// Config:
///   responseMode: "llm" | "template"
///   instructions: string         — LLM instructions or template string
///   waitForUser: bool            — if true, pause and wait for user input after responding
///   knowledgeSourceIds: string[] — additional sources for this node
/// </summary>
public class GenerateResponseNodeHandler : INodeHandler
{
    private readonly ILLMAdapter _llm;
    private readonly FlowPromptBuilder _promptBuilder;
    private readonly TemplateResolver _templateResolver;
    private readonly IKnowledgeSourceRepository _ksRepo;
    private readonly ILogger<GenerateResponseNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.GenerateResponse;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.AdvancePast;

    public GenerateResponseNodeHandler(
        ILLMAdapter llm,
        FlowPromptBuilder promptBuilder,
        TemplateResolver templateResolver,
        IKnowledgeSourceRepository ksRepo,
        ILogger<GenerateResponseNodeHandler> logger)
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
        var mode = config.TryGetProperty("responseMode", out var m) ? m.GetString() : "llm";
        var waitForUser = !config.TryGetProperty("waitForUser", out var wf) || wf.GetBoolean();

        string botResponse;

        if (mode == "template")
        {
            var template = config.TryGetProperty("instructions", out var inst)
                ? inst.GetString() ?? string.Empty
                : string.Empty;
            botResponse = _templateResolver.Resolve(template, ctx);
        }
        else
        {
            var nodeSources = await LoadNodeKnowledgeSources(config, ct);
            var systemPrompt = _promptBuilder.Build(ctx.Agent, node, ctx, nodeSources);

            var messages = new List<LLMMessage>
                { new() { Role = LLMRole.System, Content = systemPrompt } };

            foreach (var (role, content) in ctx.ConversationHistory)
            {
                if (Enum.TryParse<LLMRole>(role, ignoreCase: true, out var llmRole))
                    messages.Add(new() { Role = llmRole, Content = content });
            }

            messages.Add(new() { Role = LLMRole.User, Content = ctx.UserMessage });

            var request = new LLMRequest
            {
                Temperature = ctx.FlowDefinition.EngineSettings.ResponseTemperature,
                MaxTokens = ctx.FlowDefinition.EngineSettings.ResponseMaxTokens,
                Messages = messages
            };

            var llmResponse = await _llm.SendMessageAsync(request, ct);
            if (!llmResponse.Success)
            {
                _logger.LogWarning("GenerateResponse LLM failed for node {NodeId}", node.Id);
                botResponse = "Disculpa, ocurrió un error. ¿Puedes repetirlo?";
            }
            else
            {
                botResponse = llmResponse.Content;
            }
        }

        if (!waitForUser)
            ctx.PendingBotResponse = botResponse;

        return waitForUser
            ? NodeExecutionResult.WaitForUser(botResponse)
            : NodeExecutionResult.Advance();
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
