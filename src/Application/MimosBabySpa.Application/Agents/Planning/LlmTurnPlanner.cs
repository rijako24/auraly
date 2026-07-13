using System.Text.Encodings.Web;

using System.Text.Json;

using MimosBabySpa.Application.Agents.Configuration;

using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record TurnPlanningContext(

    AgentConfig Config,

    AgentFlowStage Stage,

    TurnPlanScope Scope,

    IReadOnlyDictionary<string, string> CurrentFacts,

    string LatestUserMessage,

    DateTimeOffset BusinessNow,

    IReadOnlyList<ChatMessage> RecentConversation,

    IReadOnlyDictionary<string, JsonElement>? StructuredContext = null);

public sealed record TurnPlanProposal(

    bool Success,

    TurnPlan? Plan,

    IReadOnlyList<string> Errors,

    int PromptTokens,

    int CompletionTokens)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ITurnPlanner

{

    Task<TurnPlanProposal> PlanAsync(TurnPlanningContext context, CancellationToken ct = default);

}

public sealed class LlmTurnPlanner : ITurnPlanner

{

    private readonly IChatClient _chatClient;

    private readonly TurnPlanValidator _validator;

    public LlmTurnPlanner(IChatClient chatClient, TurnPlanValidator validator)

    {

        _chatClient = chatClient;

        _validator = validator;

    }

    public async Task<TurnPlanProposal> PlanAsync(
        TurnPlanningContext context,
        CancellationToken ct = default)
    {
        var structuredOutput = TurnPlanJsonSchemaBuilder.Build(context.Scope);
        var prompt = BuildPrompt(context);
        var first = await CompleteAsync(prompt, structuredOutput, ct);
        if (!first.Success)
            return Failure(first.ErrorMessage ?? "Turn planner LLM call failed.", first);

        var firstValidation = ParseAndValidate(first.Content, context);

        if (firstValidation.Errors.Count == 0 && firstValidation.Plan is not null)
            return new TurnPlanProposal(true, firstValidation.Plan, [], first.PromptTokens, first.CompletionTokens)
            {
                Warnings = firstValidation.Warnings
            };

        var repairPrompt = prompt + Environment.NewLine + Environment.NewLine + JsonSerializer.Serialize(new
        {
            repair = "The previous structured plan was rejected. Return a corrected complete plan using the same JSON Schema. Do not answer the customer.",
            validationErrors = firstValidation.Errors,
            previousPlan = first.Content,
            rules = new[]
            {
                "Remove every duplicate claim.",
                "Never emit a fact or signal listed in response.ambiguousFields.",
                "Do not discard an otherwise supported signal merely because a different fact is ambiguous; preserve the full supported signal payload.",
                "ambiguousFields contains only semantically ambiguous fields mentioned by the customer, never ordinary missing stage data.",
                "Do not invent replacement values or evidence.",
                "When several facts are extracted from one utterance, keep their values semantically disjoint. Remove connectors, labels and content that belong to another extracted fact, including misspelled labels."
            }
        });

        var repaired = await CompleteAsync(repairPrompt, structuredOutput, ct);
        var promptTokens = first.PromptTokens + repaired.PromptTokens;
        var completionTokens = first.CompletionTokens + repaired.CompletionTokens;
        if (!repaired.Success)
        {
            var errors = firstValidation.Errors
                .Concat([repaired.ErrorMessage ?? "Turn plan repair call failed."]).ToList();
            var recoveryWarnings = errors.Concat(firstValidation.Warnings);
            if (firstValidation.Plan is not null
                && TryCreateFailSoftProposal(context, firstValidation.Plan, recoveryWarnings,
                    promptTokens, completionTokens, out var repairCallFallback))
                return repairCallFallback;
            return new TurnPlanProposal(false, firstValidation.Plan, errors, promptTokens, completionTokens);
        }

        var repairedValidation = ParseAndValidate(repaired.Content, context);
        if (repairedValidation.Errors.Count == 0 && repairedValidation.Plan is not null)
            return new TurnPlanProposal(true, repairedValidation.Plan, [], promptTokens, completionTokens)
            {
                Warnings = repairedValidation.Warnings
            };

        if (repairedValidation.Plan is not null
            && TryCreateFailSoftProposal(context, repairedValidation.Plan,
                repairedValidation.Errors.Concat(repairedValidation.Warnings), promptTokens, completionTokens,
                out var semanticFallback))
            return semanticFallback;

        return new TurnPlanProposal(false, repairedValidation.Plan,
            repairedValidation.Errors, promptTokens, completionTokens);
    }
    private Task<ChatCompletionResult> CompleteAsync(

        string prompt,

        ChatStructuredOutput structuredOutput,

        CancellationToken cancellationToken) =>

        _chatClient.CompleteAsync(

            messages: [ChatMessage.System(prompt)],

            options: new ChatCompletionOptions

            {

                Temperature = 0,

                MaxTokens = 3000,

                StructuredOutput = structuredOutput

            },

            cancellationToken: cancellationToken);

    private bool TryCreateFailSoftProposal(
        TurnPlanningContext context,
        TurnPlan plan,
        IEnumerable<string> warnings,
        int promptTokens,
        int completionTokens,
        out TurnPlanProposal proposal)
    {
        proposal = new TurnPlanProposal(false, plan, [], promptTokens, completionTokens);
        var validation = _validator.Validate(plan, context.Scope, context.LatestUserMessage);
        if (!TurnPlanFailSoftRecovery.TryRecover(plan, validation, context.Scope, out var recovered))
            return false;

        var recoveredValidation = _validator.Validate(recovered, context.Scope, context.LatestUserMessage);
        if (!recoveredValidation.IsValid)
            return false;

        proposal = new TurnPlanProposal(true, recovered, [], promptTokens, completionTokens)
        {
            Warnings = warnings.Concat(validation.Errors).Distinct(StringComparer.Ordinal).ToList()
        };
        return true;
    }
    private (TurnPlan? Plan, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) ParseAndValidate(

        string? content,

        TurnPlanningContext context)

    {

        if (!TurnPlanParser.TryParse(content, out var plan, out var parseError) || plan is null)

            return (null, [parseError ?? "Turn plan could not be parsed."], []);

        plan = TurnPlanNormalizer.Normalize(plan, context.Scope);
        var validation = _validator.Validate(plan, context.Scope, context.LatestUserMessage);

        return (plan, validation.Errors, []);

    }

    private static TurnPlanProposal Failure(string error, ChatCompletionResult result) =>

        new(false, null, [error], result.PromptTokens, result.CompletionTokens);

    private static string BuildPrompt(TurnPlanningContext context)

    {

        var factDefinitions = context.Scope.Facts.Values.Select(entry => new

        {

            key = entry.Key,

            role = entry.Role,

            label = entry.Label,

            type = entry.Type,

            extractionGuidance = entry.ExtractionGuidance,
            options = entry.Options.Select(option => new { option.Value, option.Label, option.Selector })

        });

        var recentConversation = context.RecentConversation

            .Where(message => !string.IsNullOrWhiteSpace(message.Content))

            .Select(message => new

            {

                role = message.Role.ToString().ToLowerInvariant(),

                content = message.Content

            });

        var flowCandidates = context.Scope.Flows.Values.Select(flow => new

        {

            id = flow.Id,

            type = flow.Type,

            routingGuidance = flow.RoutingGuidance

        });

        var payload = new

        {

            task = "Interpret the latest customer turn, including its flow intent, and return one complete turn plan matching the required JSON Schema. Do not answer the customer.",

            rules = new[]

            {

                "flowIntent.candidateFlow must be one configured flow. Prefer primaryFlow unless the latest turn clearly belongs to a secondary flow.",

                "Continue activeFlow only when the latest turn semantically continues it. Never switch flows merely because words overlap.",

                "For a non-primary flow, flowIntent.evidence must be the shortest exact contiguous quote that supports the switch. For the default primary fallback, evidence may be null.",

                "Use semantic reasoning for corrections, negations, temporal references and references to previous messages.",

                "structuredContext contains compact, authoritative, read-only runtime state when available. Use currentCart to resolve existing lines and distinguish incremental quantities from requested final quantities. Context alone never authorizes a mutation; latestUserMessage must request it.",

                "When structuredContext.shoppingContext exists, it is the authoritative record of catalog options explicitly offered during the active request. A named reference may select a product from any offer snapshot; ordinal or deictic references such as first, second, that one or this one apply only to the latest offer unless latestUserMessage names another snapshot or product.",

                "When shoppingContext.interaction.expected_reply is resolve_pending_cart_selection and latestUserMessage identifies one offered or candidate product, emit the configured cart mutation signal using that product. This is an explicit continuation of the pending add, set or remove request even when latestUserMessage contains only the product name and no mutation verb. Preserve the pending quantity when the customer does not provide a replacement quantity.",

                "Older failed searches and repeated catalog responses in recentConversation never override authoritative shoppingContext. Still distinguish selection from a genuine information request: questions about price, availability or options remain catalog queries unless the customer also asks to add, set or remove a product.",

                "When the customer explicitly contrasts alternative values or scenarios and says they are unsure which applies, do not choose either alternative. Emit no mutation for every materially disputed fact and list those fact ids in response.ambiguousFields.",

                "facts may include advanceWhenFacts and collect facts. collect means optional early capture when the customer volunteered the value; it does not define what to ask.",
                "When a fact declares options, treat each selector as an explicit alias only when the immediately preceding assistant message presented those options. Then, if latestUserMessage unambiguously refers to exactly one configured selector, emit the fact with its canonical value and do not list it in response.ambiguousFields. Before the options are presented, a bare selector or letter is not sufficient evidence; an explicit option label or semantic value may still be captured early.",

                "When latestUserMessage provides several facts in one utterance, infer their semantic boundaries. Each fact value must contain only its own value; exclude connectors, labels and content belonging to another extracted fact, even when a label is misspelled.",

                "Return sparse arrays. Include each supported fact at most once and only when the customer explicitly provides, corrects or clears it.",

                "Never invent a fact or use a system/catalog fact.",

                "Evidence must be the shortest exact contiguous quote copied from latestUserMessage. Never paraphrase evidence.",

                "For every fact and signal, confidence is your calibrated probability from 0 to 1 that the complete claim is explicitly supported by latestUserMessage after resolving context. Do not use confidence to bypass evidence requirements.",

                "Signal confidence applies to the complete payload and should reflect the least-supported item. Confidence is diagnostic metadata; semantic support and ambiguity must still be represented explicitly in the plan.",

                "Use canonical dates YYYY-MM-DD and times HH:mm based on businessNow. Apply each fact extractionGuidance exactly. Do not assume every temporal fact means the value current at businessNow; distinguish current values from values relevant to the requested service or future appointment. If that distinction is material and unclear, emit no mutation and ask for clarification.",

                "Emit a configured semantic signal at most once and only when latestUserMessage supports it. A signal describes customer meaning; it never executes an operation.",

                "Use recentConversation only to resolve contextual references in latestUserMessage. Every batch item must correspond to distinct customer meaning in latestUserMessage; preserve every independently requested item and quantity exactly once. Product ambiguity is resolved later by the deterministic operation.",
                "Never create a mutation for an entity that latestUserMessage does not explicitly reference. Words such as only or solo constrain the mentioned entity; they do not authorize removing unmentioned entities unless the customer explicitly requests that removal.",


                "When currentFacts contains a pending operation selection, interpret a short candidate choice as continuation of the configured mutation signal and identify the selected candidate at the highest specificity supported by the latest message and offered options. The deterministic operation owns restoration of the deferred batch.",

                "When the latest assistant message presented product options and explicitly asked which one to add, a short latestUserMessage that uniquely identifies one offered option is a continuation of that add request, not a new catalog search. Emit the configured cart mutation signal with that product and preserve any explicitly stated quantity; use quantity 1 only when the pending add request already established that quantity or no other quantity was stated.",

                "Ambiguity in one field never discards other explicit meaning. Still emit every independent configured signal and preserve its complete payload from latestUserMessage; report the ambiguous fact separately in response.ambiguousFields.",

                "decision must be null unless the customer explicitly accepts, rejects or revises an existing artifact.",

                "The engine owns every customer-facing response. Never draft, confirm or complete business actions; response only reports semantic ambiguity.",

                "response.ambiguousFields is only for a fact or signal that the latest customer message mentions but leaves semantically ambiguous. Never list ordinary missing stage data there. For semantic ambiguity, set response.mode to ask_clarification, list each ambiguous id, and emit no mutation or signal for it. When simply asking for a missing fact according to conversationGuidance, use response.mode continue and keep response.ambiguousFields empty."

            },

            businessNow = context.BusinessNow,

            primaryFlow = context.Scope.PrimaryFlowId,

            activeFlow = context.Scope.ActiveFlowId,

            flows = flowCandidates,

            candidateStages = context.Scope.Stages,

            stage = new

            {

                context.Stage.Id,

                context.Stage.Goal,

                context.Stage.ConversationGuidance,

                advanceWhenFacts = context.Stage.AdvanceWhenFacts,

                collect = context.Stage.Collect

            },

            allowedFacts = factDefinitions,

            allowedSignals = context.Scope.Signals.Values.Select(signal => new { signal.Type, signal.Description, valueSchema = signal.ValueSchema }),

            currentFacts = context.CurrentFacts,

            structuredContext = context.StructuredContext is { Count: > 0 }
                ? context.StructuredContext
                : null,

            recentConversation,

            latestUserMessage = context.LatestUserMessage

        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions

        {

            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping

        });

    }

}
