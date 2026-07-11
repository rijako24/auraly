using MimosBabySpa.Application.Agents.Operations;

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

        if (flows.Count(value => AgentFlowCatalog.IsPrimary(value)) != 1)
            Error(errors, "flows", "primary_flow_count", "Exactly one primary flow is required.");

        var usedOperations = new Dictionary<string, IAgentOperation>(StringComparer.OrdinalIgnoreCase);
        foreach (var flow in flows)
            ValidateFlow(config, flow, facts, usedOperations, errors);

        ValidateSignalConsistency(flows, errors);
        ValidateMessageSequences(config, errors);
        ValidateExternalEscalations(config, errors);
        ValidateOperatingHours(config, errors);
        ValidateReservationAutomations(config, usedOperations, errors);

        return errors.Count > 0
            ? new AgentConfigurationCompilation(null, errors)
            : new AgentConfigurationCompilation(
                new CompiledAgentConfiguration(config, flowMap, facts, usedOperations),
                []);
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
        var stages = UniqueBy(flow.Stages, value => value.Id, $"{path}.stages", "duplicate_stage", errors);
        foreach (var stage in flow.Stages)
        {
            var stagePath = $"{path}.stages[{stage.Id}]";
            if (stage.AllowedActions.Count > 0 || stage.EntryActions.Count > 0 || stage.AfterTool.Count > 0
                || stage.AutoSetOnSkip.Count > 0 || !string.IsNullOrWhiteSpace(stage.SkipWhen)
                || !string.IsNullOrWhiteSpace(stage.OnSuccess) || !string.IsNullOrWhiteSpace(stage.OnProblem))
            {
                Error(errors, stagePath, "legacy_stage_configuration",
                    "allowedActions, entryActions, afterTool, autoSetOnSkip, skipWhen, onSuccess and onProblem are not supported by the deterministic engine. Use signals, actions, typed outcomes and transitions.");
            }

            foreach (var fact in stage.AdvanceWhenFacts.Concat(stage.Collect).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!facts.ContainsKey(fact))
                    Error(errors, stagePath, "unknown_fact", $"Fact '{fact}' is not declared in factSchema.");
            }

            var signals = UniqueBy(stage.Signals, value => value.Type, $"{stagePath}.signals", "duplicate_signal", errors);
            foreach (var signal in stage.Signals)
                ValidateSignalSchema(signal, $"{stagePath}.signals[{signal.Type}]", errors);

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
        if (!string.IsNullOrWhiteSpace(response.Template) && !config.Templates.ContainsKey(response.Template))
            Error(errors, path, "unknown_response_template", $"Template '{response.Template}' is not configured.");
        if (!string.IsNullOrWhiteSpace(response.SendMessageSequence)
            && !config.MessageSequences.ContainsKey(response.SendMessageSequence))
            Error(errors, path, "unknown_response_sequence", $"Message sequence '{response.SendMessageSequence}' is not configured.");
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
