using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Configuration;

public sealed record AgentConfigurationDiagnostic(string Path, string Code, string Message);

public sealed record CompiledAgentConfiguration(
    AgentConfig Source,
    IReadOnlyDictionary<string, AgentFlowDefinition> Flows,
    IReadOnlyDictionary<string, FactSchemaEntry> Facts,
    IReadOnlyDictionary<string, IAgentOperation> Operations);

public sealed record AgentConfigurationCompilation(
    CompiledAgentConfiguration? Configuration,
    IReadOnlyList<AgentConfigurationDiagnostic> Diagnostics)
{
    public bool IsValid => Configuration is not null && Diagnostics.Count == 0;
}

public sealed partial class AgentConfigurationCompiler
{
    private readonly AgentOperationRegistry _operations;

    public AgentConfigurationCompiler(AgentOperationRegistry operations)
    {
        _operations = operations;
    }

    public AgentConfigurationCompilation Compile(AgentConfig config)
    {
        var errors = new List<AgentConfigurationDiagnostic>();
        var flows = AgentFlowCatalog.EffectiveFlows(config).ToList();
        var facts = UniqueBy(
            config.FactSchema,
            value => value.Key,
            "factSchema",
            "duplicate_fact",
            errors);
        var flowMap = UniqueBy(flows, value => value.Id, "flows", "duplicate_flow", errors);
        if (config.ConversationOpening.Enabled)
        {
            if (string.IsNullOrWhiteSpace(config.ConversationOpening.Guidance))
                Error(errors, "conversationOpening.guidance", "guidance_required",
                    "Conversation opening guidance is required when the policy is enabled.");
        }

        if (string.IsNullOrWhiteSpace(config.FailureResponses.LlmUnavailable))
            Error(errors, "failureResponses.llmUnavailable", "response_required",
                "A generic LLM-unavailable response is required.");

        foreach (var fact in config.FactSchema.Where(fact => fact.Options.Count > 0))
        {
            var path = $"factSchema[{fact.Key}].options";
            if (!fact.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                Error(errors, path, "canonical_options_require_string", "Canonical options require a string fact.");
            if (fact.Options.Any(option => string.IsNullOrWhiteSpace(option.Value) || string.IsNullOrWhiteSpace(option.Label)))
                Error(errors, path, "invalid_canonical_option", "Every canonical option requires a value and label.");
            if (fact.Options.Select(option => option.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fact.Options.Count)
                Error(errors, path, "duplicate_canonical_value", "Canonical option values must be unique.");
            var selectors = fact.Options.Select(option => option.Selector).Where(selector => !string.IsNullOrWhiteSpace(selector)).ToList();
            if (selectors.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectors.Count)
                Error(errors, path, "duplicate_option_selector", "Canonical option selectors must be unique.");
            if (!string.IsNullOrWhiteSpace(fact.DefaultValue)
                && !fact.Options.Any(option => option.Value.Equals(fact.DefaultValue, StringComparison.OrdinalIgnoreCase)))
                Error(errors, path, "default_outside_canonical_options", "The default value must match a configured option value.");
        }

        if (flows.Count(value => AgentFlowCatalog.IsPrimary(value)) != 1)
            Error(errors, "flows", "primary_flow_count", "Exactly one primary flow is required.");

        var usedOperations = new Dictionary<string, IAgentOperation>(StringComparer.OrdinalIgnoreCase);
        foreach (var flow in flows)
            ValidateFlow(config, flow, facts, usedOperations, errors);
        UniqueBy(config.GlobalActions, value => value.Id, "globalActions", "duplicate_global_action", errors);
        UniqueBy(config.GlobalActions, value => value.Signal.Type, "globalActions", "duplicate_global_signal", errors);
        foreach (var action in config.GlobalActions)
        {
            if (string.IsNullOrWhiteSpace(action.Signal.Type))
                Error(errors, $"globalActions[{action.Id}].signal", "missing_signal", "A global action requires one semantic signal.");
            if (action.Actions.Any(configured =>
                    !configured.Trigger.Equals(StageActionTriggers.OnSignal, StringComparison.OrdinalIgnoreCase)))
            {
                Error(errors, $"globalActions[{action.Id}].actions", "invalid_global_trigger",
                    "Global actions may only use the on_signal trigger.");
            }

            ValidateFlow(
                config,
                new AgentFlowDefinition
                {
                    Id = $"global:{action.Id}",
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = action.Id,
                            Signals = [action.Signal],
                            Actions = action.Actions,
                            Response = action.Response
                        }
                    ]
                },
                facts,
                usedOperations,
                errors);
        }

        ValidateSignalConsistency(flows, errors);
        ValidateMessageSequences(config, errors);
        ValidateExternalEscalations(config, errors);
        ValidateConversationFollowUp(config, errors);
        ValidateOperatingHours(config, errors);
        ValidateReservationAutomations(config, usedOperations, errors);
        ValidateInteractiveActions(config, usedOperations, errors);
        ValidateCommerce(config, errors);

        return errors.Count > 0
            ? new AgentConfigurationCompilation(null, errors)
            : new AgentConfigurationCompilation(
                new CompiledAgentConfiguration(config, flowMap, facts, usedOperations),
                []);
    }

    private static void ValidateCommerce(
        AgentConfig config,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var conversation = config.Commerce.Conversation;
        ValidateTerms("commerce.conversation.contextualConfirmationPhrases", conversation.ContextualConfirmationPhrases);
        ValidatePhraseRules("commerce.conversation.finalizationRules", conversation.FinalizationRules);
        ValidatePhraseRules("commerce.conversation.cartReviewRules", conversation.CartReviewRules);
        ValidatePhraseRules("commerce.conversation.productReplacementRules", conversation.ProductReplacementRules);
        ValidateTerms("commerce.conversation.candidateSelectionPhrases", conversation.CandidateSelectionPhrases);
        ValidateTerms("commerce.conversation.clauseSeparators", conversation.ClauseSeparators);
        ValidateTerms("commerce.conversation.additionalRequestPhrases", conversation.AdditionalRequestPhrases);
        ValidateTerms("commerce.pendingCart.discardOnFinalizeIssueCodes",
            config.Commerce.PendingCart.DiscardOnFinalizeIssueCodes);
        ValidateTerms("commerce.pendingCart.finalizeConfirmationPhrases",
            config.Commerce.PendingCart.FinalizeConfirmationPhrases);
        ValidatePhraseRules("commerce.pendingCart.cancellationRules",
            config.Commerce.PendingCart.CancellationRules);
        ValidateTerms("commerce.pendingCart.quantityCorrectionPhrases",
            config.Commerce.PendingCart.QuantityCorrectionPhrases);

        if (conversation.QuantityWords.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0m))
        {
            Error(errors, "commerce.conversation.quantityWords", "invalid_quantity_word",
                "Configured quantity words require a non-empty phrase and a positive quantity.");
        }

        var matching = config.Commerce.Matching;
        if (matching.ExactNameDominanceMinimumMatches < 0)
        {
            Error(errors, "commerce.matching.exactNameDominanceMinimumMatches",
                "invalid_matching_threshold", "The exact-match minimum cannot be negative.");
        }
        ValidateSimilarity("commerce.matching.candidateMentionSimilarity",
            matching.CandidateMentionSimilarity);
        ValidateSimilarity("commerce.matching.pendingReferenceSimilarity",
            matching.PendingReferenceSimilarity);
        ValidateSimilarity("commerce.matching.candidateSelectionSimilarity",
            matching.CandidateSelectionSimilarity);

        void ValidatePhraseRules(string path, IReadOnlyList<CommercePhraseRule> rules)
        {
            if (rules.Any(rule => string.IsNullOrWhiteSpace(rule.Phrase)
                    || !CommercePhraseMatchModes.All.Contains(rule.Match)))
            {
                Error(errors, path, "invalid_phrase_rule",
                    "Phrase rules require text and a supported match mode.");
            }
            if (rules.Where(rule => !string.IsNullOrWhiteSpace(rule.Phrase))
                    .Select(rule => $"{rule.Match}:{rule.Phrase}")
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != rules.Count(rule => !string.IsNullOrWhiteSpace(rule.Phrase)))
            {
                Error(errors, path, "duplicate_policy_term",
                    "Phrase rules must be unique by match mode and phrase.");
            }
        }
        void ValidateTerms(string path, IReadOnlyList<string> terms)
        {
            if (terms.Any(string.IsNullOrWhiteSpace))
            {
                Error(errors, path, "invalid_policy_term",
                    "Policy terms cannot be empty.");
            }
            if (terms.Where(term => !string.IsNullOrWhiteSpace(term))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != terms.Count(term => !string.IsNullOrWhiteSpace(term)))
            {
                Error(errors, path, "duplicate_policy_term",
                    "Policy terms must be unique.");
            }
        }

        void ValidateSimilarity(string path, double value)
        {
            if (!double.IsFinite(value) || value is < 0d or > 1d)
            {
                Error(errors, path, "invalid_matching_threshold",
                    "Similarity thresholds must be between 0 and 1.");
            }
        }
    }
    private void ValidateInteractiveActions(
        AgentConfig config,
        IDictionary<string, IAgentOperation> usedOperations,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        foreach (var (scope, outcomes) in config.InteractiveActions)
        {
            var scopePath = $"interactiveActions[{scope}]";
            if (string.IsNullOrWhiteSpace(scope))
            {
                Error(errors, scopePath, "interactive_scope_required", "Interactive action scope is required.");
                continue;
            }

            foreach (var (outcome, action) in outcomes)
            {
                var actionPath = $"{scopePath}[{outcome}]";
                if (string.IsNullOrWhiteSpace(outcome))
                {
                    Error(errors, actionPath, "interactive_outcome_required", "Interactive action outcome is required.");
                    continue;
                }

                if (!_operations.TryGet(action.Operation, out var operation))
                {
                    Error(errors, actionPath, "unknown_operation", $"Operation '{action.Operation}' is not registered.");
                    continue;
                }

                usedOperations[operation.Descriptor.Id] = operation;
                ValidateRequiredArgumentBindings(operation.Descriptor.InputSchema, action.Arguments.Keys, actionPath, errors);
                foreach (var templateId in operation.Descriptor.RequiredTemplateIds)
                {
                    if (!config.Templates.ContainsKey(templateId))
                        Error(errors, actionPath, "required_template_missing", $"Operation '{operation.Descriptor.Id}' requires template '{templateId}'.");
                }

                if (!string.IsNullOrWhiteSpace(action.SendMessageSequence)
                    && !config.MessageSequences.ContainsKey(action.SendMessageSequence))
                {
                    Error(errors, actionPath, "unknown_sequence", $"Message sequence '{action.SendMessageSequence}' is not configured.");
                }
            }
        }
    }


    private void ValidateReservationAutomations(
        AgentConfig config,
        IDictionary<string, IAgentOperation> usedOperations,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        ValidateReservationAutomation("reservationAutomations.confirmation", config.ReservationAutomations.Confirmation);
        ValidateReservationAutomation("reservationAutomations.reminder", config.ReservationAutomations.Reminder);

        void ValidateReservationAutomation(string path, ReservationAutomationConfig? automation)
        {
            if (automation is null || !automation.Enabled)
                return;

            foreach (var (outcome, action) in automation.Actions)
            {
                var actionPath = $"{path}.actions[{outcome}]";
                if (!_operations.TryGet(action.Operation, out var operation))
                {
                    Error(errors, actionPath, "unknown_operation", $"Operation '{action.Operation}' is not registered.");
                    continue;
                }

                usedOperations[operation.Descriptor.Id] = operation;
                ValidateRequiredArgumentBindings(operation.Descriptor.InputSchema, action.Arguments.Keys, actionPath, errors);
                if (!string.IsNullOrWhiteSpace(action.SendMessageSequence)
                    && !config.MessageSequences.ContainsKey(action.SendMessageSequence))
                {
                    Error(errors, actionPath, "unknown_sequence", $"Message sequence '{action.SendMessageSequence}' is not configured.");
                }
            }
        }
    }
    private void ValidateFlow(
        AgentConfig config,
        AgentFlowDefinition flow,
        IReadOnlyDictionary<string, FactSchemaEntry> facts,
        IDictionary<string, IAgentOperation> usedOperations,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var path = $"flows[{flow.Id}]";
        if (flow.TtlSeconds is <= 0)
            Error(errors, $"{path}.ttlSeconds", "invalid_flow_ttl", "ttlSeconds must be positive when configured.");
        var stages = UniqueBy(flow.Stages, value => value.Id, $"{path}.stages", "duplicate_stage", errors);
        foreach (var stage in flow.Stages)
        {
            var stagePath = $"{path}.stages[{stage.Id}]";

            foreach (var fact in stage.AdvanceWhenFacts.Concat(stage.Collect).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!facts.ContainsKey(fact))
                    Error(errors, stagePath, "unknown_fact", $"Fact '{fact}' is not declared in factSchema.");
            }

            var signals = UniqueBy(stage.Signals, value => value.Type, $"{stagePath}.signals", "duplicate_signal", errors);
            foreach (var signal in stage.Signals)
                ValidateSignalSchema(signal, $"{stagePath}.signals[{signal.Type}]", errors);
            foreach (var signal in stage.Signals)
            foreach (var rule in signal.AmbiguityRules)
            {
                var rulePath = $"{stagePath}.signals[{signal.Type}].ambiguityRules";
                if (!rule.Type.Equals("distinct_values", StringComparison.OrdinalIgnoreCase))
                    Error(errors, rulePath, "unknown_ambiguity_rule", $"Ambiguity rule '{rule.Type}' is not supported.");
                if (string.IsNullOrWhiteSpace(rule.ValueProperty))
                    Error(errors, rulePath, "value_property_required", "A distinct_values rule requires valueProperty.");
                if (rule.MinimumDistinctValues < 2)
                    Error(errors, rulePath, "invalid_minimum", "minimumDistinctValues must be at least 2.");
                if (!facts.ContainsKey(rule.Field) && !signals.ContainsKey(rule.Field))
                    Error(errors, rulePath, "unknown_ambiguous_field", $"Ambiguous field '{rule.Field}' is not declared in the stage scope.");
            }

            UniqueBy(stage.Actions, value => value.Id, $"{stagePath}.actions", "duplicate_action", errors);
            foreach (var action in stage.Actions)
                ValidateAction(config, action, facts, signals, stagePath, usedOperations, errors);

            UniqueBy(stage.Transitions, value => value.Id, $"{stagePath}.transitions", "duplicate_transition", errors);
            foreach (var transition in stage.Transitions)
            {
                var transitionPath = $"{stagePath}.transitions[{transition.Id}]";
                if (!stages.ContainsKey(transition.To))
                    Error(errors, transitionPath, "unknown_stage", $"Target stage '{transition.To}' does not exist in flow '{flow.Id}'.");
                ValidateCondition(transition.Condition, facts, signals, transitionPath, errors);
                ValidateEffects(config, transition.Effects, facts, transitionPath, errors);
            }

            ValidateResponse(config, stage.Response, stagePath, errors);
        }
    }

    private void ValidateAction(
        AgentConfig config,
        StageActionDefinition action,
        IReadOnlyDictionary<string, FactSchemaEntry> facts,
        IReadOnlyDictionary<string, StageSignalDefinition> signals,
        string stagePath,
        IDictionary<string, IAgentOperation> usedOperations,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var path = $"{stagePath}.actions[{action.Id}]";
        if (!StageActionTriggers.All.Contains(action.Trigger))
            Error(errors, path, "unknown_trigger", $"Trigger '{action.Trigger}' is not supported.");
        if (action.Trigger.Equals(StageActionTriggers.OnSignal, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(action.Signal))
            Error(errors, path, "signal_required", "on_signal actions require a signal id.");
        if (!string.IsNullOrWhiteSpace(action.Signal) && !signals.ContainsKey(action.Signal))
            Error(errors, path, "unknown_signal", $"Action references undeclared signal '{action.Signal}'.");
        if (action.Execution.TimeoutSeconds <= 0 || action.Execution.MaxAttempts <= 0)
            Error(errors, path, "invalid_execution_policy", "timeoutSeconds and maxAttempts must be positive.");
        if (!StageActionIdempotency.All.Contains(action.Execution.Idempotency))
            Error(errors, path, "invalid_idempotency", $"Idempotency '{action.Execution.Idempotency}' is not supported.");
        if (action.Execution.MaxAttempts > 1
            && action.Execution.Idempotency.Equals(StageActionIdempotency.None, StringComparison.OrdinalIgnoreCase))
            Error(errors, path, "retry_requires_idempotency", "maxAttempts greater than one requires an idempotency policy.");

        if (!_operations.TryGet(action.Operation, out var operation))
        {
            Error(errors, path, "unknown_operation", $"Operation '{action.Operation}' is not registered.");
            return;
        }

        usedOperations[operation.Descriptor.Id] = operation;
        ValidateRequiredArgumentBindings(operation.Descriptor.InputSchema, action.Arguments.Keys, path, errors);
        ValidateCondition(action.Condition, facts, signals, path, errors);
        foreach (var (outcomeCode, handler) in action.OnOutcome)
        {
            var outcomePath = $"{path}.onOutcome[{outcomeCode}]";
            if (!operation.Descriptor.OutcomeCodes.Contains(outcomeCode, StringComparer.OrdinalIgnoreCase))
                Error(errors, outcomePath, "unknown_outcome", $"Operation '{action.Operation}' does not declare outcome '{outcomeCode}'.");
            ValidateEffects(config, handler.Effects, facts, outcomePath, errors);
            ValidateResponse(config, handler.Response, outcomePath, errors);
            if (handler.Response?.AwaitCustomerReply == true
                && handler.Effects.Any(effect =>
                    effect.Type.Equals(StageEffectTypes.CompleteRequest, StringComparison.OrdinalIgnoreCase)
                    || effect.Type.Equals(StageEffectTypes.EscalateHuman, StringComparison.OrdinalIgnoreCase)))
            {
                Error(errors, outcomePath, "terminal_response_cannot_await_reply",
                    "A response cannot await a customer reply after completing or escalating the request.");
            }
        }

        foreach (var template in operation.Descriptor.RequiredTemplateIds)
        {
            if (!config.Templates.ContainsKey(template))
                Error(errors, path, "missing_operation_template", $"Operation '{action.Operation}' requires template '{template}'.");
        }
    }

    private static void ValidateSignalConsistency(
        IReadOnlyList<AgentFlowDefinition> flows,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var schemas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in flows.SelectMany(flow => flow.Stages).SelectMany(stage => stage.Signals))
        {
            if (string.IsNullOrWhiteSpace(signal.Type) || signal.ValueSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;
            var schema = signal.ValueSchema.GetRawText();
            if (schemas.TryGetValue(signal.Type, out var existing) && existing != schema)
                Error(errors, "flows.signals", "conflicting_signal_schema", $"Signal '{signal.Type}' has different valueSchema definitions across stages.");
            else
                schemas[signal.Type] = schema;
        }
    }
    private static void ValidateSignalSchema(
        StageSignalDefinition signal,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (signal.ValueSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            Error(errors, path, "signal_schema_required", "A signal requires a valueSchema JSON object.");
            return;
        }

        if (!signal.ValueSchema.TryGetProperty("type", out _)
            && !signal.ValueSchema.TryGetProperty("anyOf", out _))
            Error(errors, path, "signal_schema_type_required", "Signal valueSchema requires type or anyOf.");

        ValidateStrictObjectSchemas(signal.ValueSchema, path, errors);
    }

    private static void ValidateStrictObjectSchemas(
        System.Text.Json.JsonElement schema,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (schema.ValueKind != System.Text.Json.JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == System.Text.Json.JsonValueKind.String
            && type.GetString() == "object")
        {
            if (!schema.TryGetProperty("additionalProperties", out var additional)
                || additional.ValueKind != System.Text.Json.JsonValueKind.False)
                Error(errors, path, "strict_object_required", "Object signal schemas require additionalProperties=false.");

            if (schema.TryGetProperty("properties", out var objectProperties)
                && objectProperties.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var propertyNames = objectProperties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                var requiredNames = schema.TryGetProperty("required", out var required)
                    && required.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? required.EnumerateArray().Select(value => value.GetString()).Where(value => value is not null).Cast<string>().ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                if (!propertyNames.SetEquals(requiredNames))
                    Error(errors, path, "strict_required_properties", "Strict object signal schemas must list every property in required.");
            }
        }

        if (schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var property in properties.EnumerateObject())
                ValidateStrictObjectSchemas(property.Value, $"{path}.properties[{property.Name}]", errors);

        if (schema.TryGetProperty("items", out var items))
            ValidateStrictObjectSchemas(items, $"{path}.items", errors);
        if (schema.TryGetProperty("anyOf", out var anyOf)
            && anyOf.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var branch in anyOf.EnumerateArray())
                ValidateStrictObjectSchemas(branch, $"{path}.anyOf", errors);
    }
    private static void ValidateRequiredArgumentBindings(
        string inputSchema,
        IEnumerable<string> configuredArguments,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        try
        {
            using var schema = System.Text.Json.JsonDocument.Parse(inputSchema);
            if (!schema.RootElement.TryGetProperty("required", out var required)
                || required.ValueKind != System.Text.Json.JsonValueKind.Array)
                return;

            var configured = configuredArguments.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var input in required.EnumerateArray()
                         .Select(value => value.GetString())
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!configured.Contains(input!))
                    Error(errors, path, "required_argument_binding_missing", $"Required operation input '{input}' has no configured argument binding.");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            Error(errors, path, "invalid_operation_input_schema", "The registered operation input schema is invalid JSON.");
        }
    }

    private static void ValidateCondition(
        StageConditionDefinition? condition,
        IReadOnlyDictionary<string, FactSchemaEntry> facts,
        IReadOnlyDictionary<string, StageSignalDefinition> signals,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (condition is null)
            return;
        foreach (var nested in condition.All.Concat(condition.Any))
            ValidateCondition(nested, facts, signals, path, errors);
        ValidateCondition(condition.Not, facts, signals, path, errors);

        var referenced = new[]
        {
            condition.FactPresent,
            condition.FactMissing,
            condition.FactChanged,
            condition.FactEquals?.Key
        };
        if (!string.IsNullOrWhiteSpace(condition.SignalPresent) && !signals.ContainsKey(condition.SignalPresent))
            Error(errors, path, "unknown_condition_signal", $"Condition references unknown signal '{condition.SignalPresent}'.");

        foreach (var fact in referenced.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!facts.ContainsKey(fact!))
                Error(errors, path, "unknown_condition_fact", $"Condition references unknown fact '{fact}'.");
        }
    }

    private static void ValidateEffects(
        AgentConfig config,
        IReadOnlyList<StageEffectDefinition> effects,
        IReadOnlyDictionary<string, FactSchemaEntry> facts,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        foreach (var effect in effects)
        {
            if (!StageEffectTypes.All.Contains(effect.Type))
            {
                Error(errors, path, "unknown_effect", $"Effect '{effect.Type}' is not supported.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(effect.Fact) && !facts.ContainsKey(effect.Fact))
                Error(errors, path, "unknown_effect_fact", $"Effect references unknown fact '{effect.Fact}'.");
            foreach (var fact in effect.Facts.Concat(effect.Bindings.Keys))
            {
                if (!facts.ContainsKey(fact))
                    Error(errors, path, "unknown_effect_fact", $"Effect references unknown fact '{fact}'.");
            }
            if (!string.IsNullOrWhiteSpace(effect.Template) && !config.Templates.ContainsKey(effect.Template))
                Error(errors, path, "unknown_template", $"Template '{effect.Template}' is not configured.");
            if (!string.IsNullOrWhiteSpace(effect.Sequence) && !config.MessageSequences.ContainsKey(effect.Sequence))
                Error(errors, path, "unknown_sequence", $"Message sequence '{effect.Sequence}' is not configured.");
        }
    }

    private static void ValidateResponse(
        AgentConfig config,
        StageResponseDefinition? response,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (response is null)
            return;
        if (response.AwaitCustomerReply && !config.ConversationFollowUp.Enabled)
            Error(errors, path, "conversation_follow_up_disabled",
                "awaitCustomerReply requires conversationFollowUp.enabled=true.");
        if (response.AwaitCustomerReply
            && response.SuppressText
            && string.IsNullOrWhiteSpace(response.SendMessageSequence))
            Error(errors, path, "await_reply_requires_visible_response",
                "awaitCustomerReply requires a customer-visible response or message sequence.");
        if (!string.IsNullOrWhiteSpace(response.Template) && !config.Templates.ContainsKey(response.Template))
            Error(errors, path, "unknown_response_template", $"Template '{response.Template}' is not configured.");
        if (!string.IsNullOrWhiteSpace(response.SendMessageSequence)
            && !config.MessageSequences.ContainsKey(response.SendMessageSequence))
            Error(errors, path, "unknown_response_sequence", $"Message sequence '{response.SendMessageSequence}' is not configured.");
    }

    private static void ValidateConversationFollowUp(
        AgentConfig config,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var followUp = config.ConversationFollowUp;
        if (!followUp.Enabled)
            return;

        if (followUp.DelayMinutes is < 1 or > 43200)
            Error(errors, "conversationFollowUp.delayMinutes", "invalid_follow_up_delay",
                "delayMinutes must be between 1 minute and 30 days.");
        if (string.IsNullOrWhiteSpace(followUp.Guidance)
            && string.IsNullOrWhiteSpace(followUp.FallbackSequence))
            Error(errors, "conversationFollowUp", "follow_up_content_required",
                "Contextual guidance or a fallback sequence is required.");
        if (!string.IsNullOrWhiteSpace(followUp.FallbackSequence)
            && !config.MessageSequences.ContainsKey(followUp.FallbackSequence))
            Error(errors, "conversationFollowUp.fallbackSequence", "unknown_sequence",
                $"Message sequence '{followUp.FallbackSequence}' is not configured.");
    }

    private static void ValidateOperatingHours(
        AgentConfig config,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (!config.OperatingHours.Enforce)
            return;
        var response = config.OperatingHours.OutsideHours;
        if (string.IsNullOrWhiteSpace(response.Guidance)
            && string.IsNullOrWhiteSpace(response.Template)
            && string.IsNullOrWhiteSpace(response.SendMessageSequence))
            Error(errors, "operatingHours.outsideHours", "response_required", "A guidance, template or message sequence is required when operating hours are enforced.");
        if (!string.IsNullOrWhiteSpace(response.Template) && !config.Templates.ContainsKey(response.Template))
            Error(errors, "operatingHours.outsideHours.template", "unknown_template", $"Template '{response.Template}' is not configured.");
        if (!string.IsNullOrWhiteSpace(response.SendMessageSequence)
            && !config.MessageSequences.ContainsKey(response.SendMessageSequence))
            Error(errors, "operatingHours.outsideHours.sendMessageSequence", "unknown_sequence", $"Message sequence '{response.SendMessageSequence}' is not configured.");
    }

    private static Dictionary<string, T> UniqueBy<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string path,
        string code,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var key = keySelector(value)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                Error(errors, path, "id_required", "A non-empty id/key is required.");
                continue;
            }
            if (!result.TryAdd(key, value))
                Error(errors, path, code, $"'{key}' is declared more than once.");
        }
        return result;
    }

    private static void Error(
        ICollection<AgentConfigurationDiagnostic> errors,
        string path,
        string code,
        string message) =>
        errors.Add(new AgentConfigurationDiagnostic(path, code, message));
}
