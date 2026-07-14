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
    IReadOnlyList<ChatMessage> RecentConversation,
    bool RequestOpeningRequired = false);

public sealed record DeterministicRenderedResponse(
    string Text,
    int PromptTokens,
    int CompletionTokens,
    bool Success = true,
    string? ErrorMessage = null);

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
        var opening = request.RequestOpeningRequired
            ? await RenderConversationOpeningAsync(request, cancellationToken)
            : new DeterministicRenderedResponse(string.Empty, 0, 0);
        if (!opening.Success)
            return opening;
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
                Combine(opening.Text, _presentations.Compose(request.Config, null, presentations)),
                opening.PromptTokens,
                opening.CompletionTokens);
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
        var factDefinitions = request.Config.FactSchema.ToDictionary(
            fact => fact.Key,
            StringComparer.OrdinalIgnoreCase);
        var pendingStageBlockers = request.Stage.AdvanceWhenFacts
            .Where(key => !StageAdvanceFactReadiness.IsSatisfied(
                key,
                request.Turn.Facts,
                request.Config.FactSchema))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                factDefinitions.TryGetValue(key, out var definition);
                var source = definition?.Source ?? "user";
                return new
                {
                    key,
                    label = string.IsNullOrWhiteSpace(definition?.Label) ? key : definition.Label,
                    guidance = definition?.ExtractionGuidance,
                    source,
                    canBeProvidedByCustomer = source.Equals("user", StringComparison.OrdinalIgnoreCase)
                };
            })
            .ToList();
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
                "stageReadiness.pendingBlockers is authoritative only for current-stage readiness. It does not decide what to ask and never proves that the whole request is complete.",
                "Use responseGuidance and stage conversationGuidance to decide what to request. When they require a pending blocker whose canBeProvidedByCustomer is true, present it as necessary rather than optional. Never ask the customer to provide blockers whose canBeProvidedByCustomer is false. When pendingBlockers is empty, do not invent missing requirements.",
                "When responseGuidance.mode is ask_clarification, ask only for the named ambiguity and never say that all data is ready or that the process can continue as complete.",
                "Never describe a cart mutation as applied unless a successful commerce operation outcome in this turn supports it; conversation history and the user's request are not proof of execution.",
                "Apply the configured persona and policies as the authority for voice, empathy, conversational style and WhatsApp presentation.",
                "Be concise, natural and consistent with the configured persona."
            },
            stage = new
            {
                request.Stage.Id,
                request.Stage.Goal,
                request.Stage.ConversationGuidance
            },
            responseGuidance = request.Turn.Response,
            stageReadiness = new
            {
                usesAdvanceFacts = request.Stage.AdvanceWhenFacts.Count > 0,
                pendingBlockers = pendingStageBlockers
            },
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
                MaxTokens = 1200
            },
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return new DeterministicRenderedResponse(
                string.Empty, result.PromptTokens, result.CompletionTokens, false,
                result.ErrorMessage ?? "LLM response rendering failed.");
        }

        var response = result.Content;
        return new DeterministicRenderedResponse(
            Combine(opening.Text, _presentations.Compose(request.Config, response, presentations)),
            opening.PromptTokens + result.PromptTokens,
            opening.CompletionTokens + result.CompletionTokens);
    }
    private async Task<DeterministicRenderedResponse> RenderConversationOpeningAsync(
        DeterministicResponseRequest request,
        CancellationToken cancellationToken)
    {
        var policy = request.Config.ConversationOpening;
        var payload = new
        {
            task = "Write only a brief, natural opening for the new customer request. Return only the opening text.",
            persona = request.Config.BasePrompt,
            guidance = policy.Guidance,
            allowQuestions = policy.AllowQuestions,
            rules = new[]
            {
                "Do not mention internal state, requests, generations, stages, tools or configuration.",
                "Do not claim catalog, availability, prices, totals, reservations, payments or completed actions.",
                "Do not repeat the substantive answer or ask for data; the deterministic response that follows owns the next step.",
                "When allowQuestions is false, do not include any question.",
                "Use remembered customer facts only to personalize naturally.",
                "Keep the opening concise and appropriate for WhatsApp."
            },
            facts = request.Turn.Facts,
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
                MaxTokens = 160
            },
            cancellationToken: cancellationToken);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
        {
            var opening = result.Content.Trim();
            if (policy.AllowQuestions || (!opening.Contains('?') && !opening.Contains('¿')))
                return new DeterministicRenderedResponse(opening, result.PromptTokens, result.CompletionTokens);
        }

        var reason = result.Success
            ? "LLM opening violated the configured presentation policy."
            : result.ErrorMessage ?? "LLM opening rendering failed.";
        return new DeterministicRenderedResponse(
            string.Empty, result.PromptTokens, result.CompletionTokens, false, reason);
    }

    private static string Combine(string prefix, string response)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return response.Trim();
        if (string.IsNullOrWhiteSpace(response))
            return prefix.Trim();
        return $"{prefix.Trim()}{Environment.NewLine}{Environment.NewLine}{response.Trim()}";
    }
}
