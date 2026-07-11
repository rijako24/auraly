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

    IReadOnlyList<ChatMessage> RecentConversation);

public sealed record TurnPlanProposal(

    bool Success,

    TurnPlan? Plan,

    IReadOnlyList<string> Errors,

    int PromptTokens,

    int CompletionTokens);

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

    public async Task<TurnPlanProposal> PlanAsync(TurnPlanningContext context, CancellationToken ct = default)

    {

        var structuredOutput = TurnPlanJsonSchemaBuilder.Build(context.Scope);

        var prompt = BuildPrompt(context);

        var first = await CompleteAsync(prompt, structuredOutput, ct);

        if (!first.Success)

            return Failure(first.ErrorMessage ?? "Turn planner LLM call failed.", first);

        var firstValidation = ParseAndValidate(first.Content, context);

        if (firstValidation.Errors.Count == 0 && firstValidation.Plan is not null)

        {

            return new TurnPlanProposal(

                true,

                firstValidation.Plan,

                [],

                first.PromptTokens,

                first.CompletionTokens);

        }

        if (firstValidation.Plan is not null
            && firstValidation.Errors.Count > 0
            && firstValidation.Errors.All(error => error.Contains("selector", StringComparison.OrdinalIgnoreCase)))
        {
            return await RepairOptionSelectionAsync(context, firstValidation.Plan, first, ct);
        }
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

                "Do not invent replacement values or evidence."

            }

        });

        var repaired = await CompleteAsync(repairPrompt, structuredOutput, ct);

        if (!repaired.Success)

        {

            return new TurnPlanProposal(

                false,

                firstValidation.Plan,

                firstValidation.Errors.Concat([repaired.ErrorMessage ?? "Turn plan repair call failed."]).ToList(),

                first.PromptTokens + repaired.PromptTokens,

                first.CompletionTokens + repaired.CompletionTokens);

        }

        var repairedValidation = ParseAndValidate(repaired.Content, context);

        return new TurnPlanProposal(

            repairedValidation.Errors.Count == 0 && repairedValidation.Plan is not null,

            repairedValidation.Plan,

            repairedValidation.Errors,

            first.PromptTokens + repaired.PromptTokens,

            first.CompletionTokens + repaired.CompletionTokens);

    }

    private async Task<TurnPlanProposal> RepairOptionSelectionAsync(
        TurnPlanningContext context,
        TurnPlan previousPlan,
        ChatCompletionResult first,
        CancellationToken cancellationToken)
    {
        var references = OptionSelectorReferenceDetector.Find(context.Scope, context.LatestUserMessage);
        if (references.Count != 1)
            return new TurnPlanProposal(false, previousPlan, ["Option selector reference is not unique."], first.PromptTokens, first.CompletionTokens);

        var fact = references[0].Fact;
        var values = fact.Options.Select(option => option.Value).ToArray();
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["selectedValue"] = new Dictionary<string, object?>
                {
                    ["anyOf"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "string", ["enum"] = values },
                        new Dictionary<string, object?> { ["type"] = "null" }
                    }
                }
            },
            ["required"] = new[] { "selectedValue" }
        };
        var structuredOutput = new ChatStructuredOutput
        {
            Name = "option_selection",
            Description = "Canonical selection for one configured option fact.",
            JsonSchema = JsonSerializer.Serialize(schema),
            Strict = true
        };
        var prompt = JsonSerializer.Serialize(new
        {
            task = "Interpret whether the latest customer turn selects one configured option. Return its canonical value or null.",
            fact = new
            {
                fact.Key,
                fact.Label,
                fact.ExtractionGuidance,
                options = fact.Options.Select(option => new { option.Value, option.Label, option.Selector })
            },
            stage = new { context.Stage.Id, context.Stage.Goal, context.Stage.ConversationGuidance },
            recentConversation = context.RecentConversation.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content
            }),
            latestUserMessage = context.LatestUserMessage
        });
        var focused = await CompleteAsync(prompt, structuredOutput, cancellationToken);
        if (!focused.Success)
        {
            return new TurnPlanProposal(
                false, previousPlan, [focused.ErrorMessage ?? "Option selection repair failed."],
                first.PromptTokens + focused.PromptTokens,
                first.CompletionTokens + focused.CompletionTokens);
        }

        try
        {
            using var document = JsonDocument.Parse(focused.Content ?? string.Empty);
            var root = document.RootElement;
            if (!root.TryGetProperty("selectedValue", out var selected)
                || selected.ValueKind != JsonValueKind.String)
            {
                return new TurnPlanProposal(
                    false, previousPlan, ["Option selection repair did not resolve a canonical value."],
                    first.PromptTokens + focused.PromptTokens,
                    first.CompletionTokens + focused.CompletionTokens);
            }

            var selectedValue = selected.GetString() ?? string.Empty;
            var referencedOption = references[0].Option;
            if (!selectedValue.Equals(referencedOption.Value, StringComparison.OrdinalIgnoreCase))
            {
                return new TurnPlanProposal(
                    false, previousPlan, ["Option selection repair did not confirm the referenced configured option."],
                    first.PromptTokens + focused.PromptTokens,
                    first.CompletionTokens + focused.CompletionTokens);
            }
            var evidence = referencedOption.Selector!;

            var remainingAmbiguities = previousPlan.Response.AmbiguousFields
                .Where(field => !field.Equals(fact.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var repairedPlan = new TurnPlan
            {
                FlowIntent = previousPlan.FlowIntent,
                Facts = previousPlan.Facts
                    .Where(claim => !claim.Key.Equals(fact.Key, StringComparison.OrdinalIgnoreCase))
                    .Append(new PlannedFactClaim
                    {
                        Key = fact.Key,
                        Operation = TurnPlanOperations.Set,
                        Value = JsonSerializer.SerializeToElement(selectedValue),
                        Evidence = evidence
                    })
                    .ToList(),
                Signals = previousPlan.Signals,
                Decision = previousPlan.Decision,
                Response = new TurnPlanResponseDirective
                {
                    Mode = remainingAmbiguities.Count == 0 ? "continue" : "ask_clarification",
                    AmbiguousFields = remainingAmbiguities
                }
            };
            var validation = _validator.Validate(repairedPlan, context.Scope, context.LatestUserMessage);
            return new TurnPlanProposal(
                validation.IsValid,
                repairedPlan,
                validation.Errors,
                first.PromptTokens + focused.PromptTokens,
                first.CompletionTokens + focused.CompletionTokens);
        }
        catch (JsonException exception)
        {
            return new TurnPlanProposal(
                false, previousPlan, [exception.Message],
                first.PromptTokens + focused.PromptTokens,
                first.CompletionTokens + focused.CompletionTokens);
        }
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

    private (TurnPlan? Plan, IReadOnlyList<string> Errors) ParseAndValidate(

        string? content,

        TurnPlanningContext context)

    {

        if (!TurnPlanParser.TryParse(content, out var plan, out var parseError) || plan is null)

            return (null, [parseError ?? "Turn plan could not be parsed."]);

        plan = TurnPlanNormalizer.Normalize(plan, context.Scope);
        var validation = _validator.Validate(plan, context.Scope, context.LatestUserMessage);

        return (plan, validation.Errors);

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

                "When the customer explicitly contrasts alternative values or scenarios and says they are unsure which applies, do not choose either alternative. Emit no mutation for every materially disputed fact and list those fact ids in response.ambiguousFields.",

                "facts may include advanceWhenFacts and collect facts. collect means optional early capture when the customer volunteered the value; it does not define what to ask.",
                "When a fact declares options, resolve references to a configured selector and return the corresponding canonical value.",

                "Return sparse arrays. Include each supported fact at most once and only when the customer explicitly provides, corrects or clears it.",

                "Never invent a fact or use a system/catalog fact.",

                "Evidence must be the shortest exact contiguous quote copied from latestUserMessage. Never paraphrase evidence.",

                "Use canonical dates YYYY-MM-DD and times HH:mm based on businessNow. Apply each fact extractionGuidance exactly. Do not assume every temporal fact means the value current at businessNow; distinguish current values from values relevant to the requested service or future appointment. If that distinction is material and unclear, emit no mutation and ask for clarification.",

                "Emit a configured semantic signal at most once and only when latestUserMessage supports it. A signal describes customer meaning; it never executes an operation.",

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

            recentConversation,

            latestUserMessage = context.LatestUserMessage

        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions

        {

            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping

        });

    }

}
