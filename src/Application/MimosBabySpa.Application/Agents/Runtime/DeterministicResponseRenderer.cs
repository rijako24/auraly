using System.Text.Encodings.Web;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed record DeterministicResponseRequest(
    AgentConfig Config,
    AgentFlowStage Stage,
    DeterministicTurnResult Turn,
    string LatestUserMessage,
    IReadOnlyList<ChatMessage> RecentConversation);

public sealed record DeterministicRenderedResponse(string Text, int PromptTokens, int CompletionTokens);

public interface IDeterministicResponseRenderer
{
    Task<DeterministicRenderedResponse> RenderAsync(DeterministicResponseRequest request, CancellationToken cancellationToken = default);
}

public sealed class DeterministicResponseRenderer : IDeterministicResponseRenderer
{
    private readonly IChatClient _chat;
    private readonly IOperationPresentationComposer _presentations;

    public DeterministicResponseRenderer(IChatClient chat, IOperationPresentationComposer presentations)
    {
        _chat = chat;
        _presentations = presentations;
    }

    public async Task<DeterministicRenderedResponse> RenderAsync(
        DeterministicResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        var presentations = request.Turn.Presentations.ToList();
        if (request.Turn.Response?.SuppressText == true)
            return new DeterministicRenderedResponse(string.Empty, 0, 0);
        if (!string.IsNullOrWhiteSpace(request.Turn.Response?.Template)
            && !presentations.Any(presentation =>
                presentation.Mode == FragmentRenderMode.Exclusive
                && presentation.TemplateId.Equals(request.Turn.Response.Template, StringComparison.OrdinalIgnoreCase)))
        {
            presentations.Add(new OperationPresentation(
                request.Turn.Response.Template,
                request.Turn.Facts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase),
                FragmentRenderMode.Exclusive,
                FragmentPriority.Required));
        }

        if (presentations.Count == 0
            && !string.IsNullOrWhiteSpace(request.Turn.Response?.FallbackTemplate))
        {
            presentations.Add(new OperationPresentation(
                request.Turn.Response.FallbackTemplate,
                request.Turn.Facts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase),
                FragmentRenderMode.Exclusive,
                FragmentPriority.Required));
        }
        if (presentations.Any(presentation => presentation.Mode == FragmentRenderMode.Exclusive))
        {
            return new DeterministicRenderedResponse(
                _presentations.Compose(request.Config, null, presentations),
                0,
                0);
        }

        var outcomes = request.Turn.Trace
            .Where(trace => !trace.Skipped && trace.Outcome is not null)
            .Select(trace => new
            {
                action = trace.ActionId,
                code = trace.OutcomeCode,
                success = trace.Success,
                data = trace.Outcome!.Data,
                error = trace.Outcome.Error
            });
        var payload = new
        {
            task = "Write the customer-facing reply for this completed deterministic turn. Return only the reply text.",
            persona = request.Config.BasePrompt,
            rules = new[]
            {
                "Do not execute, propose or mention operations, tools, JSON, facts, stages or internal state.",
                "Treat operation outcomes as the only authority for catalog, availability, prices, totals, reservations, payments and external state.",
                "Never claim success, confirmation, availability or payment unless a successful outcome explicitly supports it.",
                "Follow responseGuidance and stage conversationGuidance. Ask only for data those instructions require.",
                "Be concise, natural and consistent with the configured persona."
            },
            stage = new
            {
                request.Stage.Id,
                request.Stage.Goal,
                request.Stage.ConversationGuidance
            },
            responseGuidance = request.Turn.Response,
            facts = request.Turn.Facts,
            outcomes,
            recentConversation = request.RecentConversation.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content
            }),
            latestUserMessage = request.LatestUserMessage
        };
        var prompt = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var result = await _chat.CompleteAsync(
            [ChatMessage.System(prompt)],
            options: new ChatCompletionOptions
            {
                Temperature = request.Config.Temperature,
                MaxTokens = 1200,},
            cancellationToken: cancellationToken);

        var response = result.Success ? result.Content : string.Empty;
        return new DeterministicRenderedResponse(
            _presentations.Compose(request.Config, response, presentations),
            result.PromptTokens,
            result.CompletionTokens);
    }
}
