using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Templates;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed record SemanticSignal(string Type, JsonElement Value, string Evidence);

public sealed class DeterministicStageExecutionContext
{
    public OperationContext OperationContext { get; init; } = null!;
    public Dictionary<string, string> Facts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<SemanticSignal> Signals { get; init; } = [];
    public string LatestUserMessage { get; init; } = string.Empty;
    public IReadOnlySet<string> ChangedFacts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ActiveVerifications { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public ISet<string> ExecutedActionKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record StageOperationTrace(
    string ActionId,
    string OperationId,
    string ArgumentsJson,
    string OutcomeCode,
    bool Success,
    bool Skipped = false,
    string? SkipReason = null,
    OperationOutcome? Outcome = null);

public sealed class DeterministicStageExecutionResult
{
    public IReadOnlyList<StageOperationTrace> Trace { get; init; } = [];
    public IReadOnlyList<OperationPresentation> Presentations { get; init; } = [];
    public IReadOnlyList<OperationEffect> OperationEffects { get; init; } = [];
    public IReadOnlyList<string> Sequences { get; init; } = [];
    public IReadOnlyList<string> Events { get; init; } = [];
    public IReadOnlyList<OperationEvent> DomainEvents { get; init; } = [];
    public IReadOnlyDictionary<string, string?> FactMutations { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    public bool EscalateToHuman { get; init; }
    public bool RequestCompleted { get; init; }
    public StageResponseDefinition? Response { get; init; }
}

public sealed class DeterministicStageExecutor
{
    private readonly AgentOperationRegistry _operations;
    private readonly StageConditionEvaluator _conditions;
    private readonly OperationArgumentBinder _arguments;

    public DeterministicStageExecutor(
        AgentOperationRegistry operations,
        StageConditionEvaluator conditions,
        OperationArgumentBinder arguments)
    {
        _operations = operations;
        _conditions = conditions;
        _arguments = arguments;
    }

    public async Task<DeterministicStageExecutionResult> ExecuteAsync(
        AgentFlowStage stage,
        string trigger,
        DeterministicStageExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var trace = new List<StageOperationTrace>();
        var presentations = new List<OperationPresentation>();
        var operationEffects = new List<OperationEffect>();
        var sequences = new List<string>();
        var events = new List<string>();
        var domainEvents = new List<OperationEvent>();
        var factMutations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        StageResponseDefinition? response = null;
        var escalate = false;
        var completed = false;

        foreach (var action in stage.Actions.Where(value =>
                     value.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
        {
            if (!_conditions.Evaluate(action.Condition, context, action.Signal))
            {
                trace.Add(new StageOperationTrace(action.Id, action.Operation, "{}", string.Empty, false, true, "condition_not_met"));
                continue;
            }

            if (!_operations.TryGet(action.Operation, out var operation))
                throw new InvalidOperationException($"Compiled operation '{action.Operation}' is not registered.");

            var boundArguments = _arguments.Bind(action.Arguments, context);
            var argumentsJson = boundArguments.GetRawText();
            if (!RequiredInputsPresent(operation.Descriptor.InputSchema, boundArguments))
            {
                trace.Add(new StageOperationTrace(action.Id, action.Operation, argumentsJson, string.Empty, false, true, "required_inputs_missing"));
                continue;
            }
            var executionKey = $"{stage.Id}:{action.Id}:{StableHash(argumentsJson)}";
            if (!context.ExecutedActionKeys.Add(executionKey))
            {
                trace.Add(new StageOperationTrace(action.Id, action.Operation, argumentsJson, string.Empty, false, true, "idempotent_replay"));
                continue;
            }

            var outcome = await operation.ExecuteAsync(boundArguments, context.OperationContext, cancellationToken);
            trace.Add(new StageOperationTrace(action.Id, action.Operation, argumentsJson, outcome.Code, outcome.Success, Outcome: outcome));
            presentations.AddRange(outcome.Presentations);
            operationEffects.AddRange(outcome.Effects);
            events.AddRange(outcome.Events);
            domainEvents.AddRange(outcome.DomainEvents);

            if (!action.OnOutcome.TryGetValue(outcome.Code, out var handler))
                continue;

            response = handler.Response ?? response;
            foreach (var effect in handler.Effects)
            {
                switch (effect.Type)
                {
                    case StageEffectTypes.SetFact:
                        if (!string.IsNullOrWhiteSpace(effect.Fact))
                            factMutations[effect.Fact] = ElementText(effect.Value);
                        break;
                    case StageEffectTypes.SetFactsFromOutcome:
                        foreach (var (fact, dataPath) in effect.Bindings)
                            factMutations[fact] = ReadPath(outcome.Data, dataPath);
                        break;
                    case StageEffectTypes.ClearFacts:
                        foreach (var fact in effect.Facts)
                            factMutations[fact] = null;
                        break;
                    case StageEffectTypes.AddPresentation:
                        if (!string.IsNullOrWhiteSpace(effect.Template))
                        {
                            presentations.Add(new OperationPresentation(
                                effect.Template,
                                ReadPresentationData(outcome.Data, effect.DataPath),
                                ParseMode(effect.Mode),
                                ParsePriority(effect.Priority)));
                        }
                        break;
                    case StageEffectTypes.EnqueueSequence:
                        if (!string.IsNullOrWhiteSpace(effect.Sequence))
                            sequences.Add(effect.Sequence);
                        break;
                    case StageEffectTypes.EmitEvent:
                        if (!string.IsNullOrWhiteSpace(effect.Event))
                            events.Add(effect.Event);
                        break;
                    case StageEffectTypes.EscalateHuman:
                        escalate = true;
                        break;
                    case StageEffectTypes.CompleteRequest:
                        completed = true;
                        break;
                }
            }
        }

        return new DeterministicStageExecutionResult
        {
            Trace = trace,
            Presentations = presentations,
            OperationEffects = operationEffects,
            Sequences = sequences.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Events = events.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            DomainEvents = domainEvents,
            FactMutations = factMutations,
            EscalateToHuman = escalate,
            RequestCompleted = completed,
            Response = response
        };
    }

    private static bool RequiredInputsPresent(string inputSchema, JsonElement arguments)
    {
        try
        {
            using var schema = JsonDocument.Parse(inputSchema);
            if (!schema.RootElement.TryGetProperty("required", out var required)
                || required.ValueKind != JsonValueKind.Array)
                return true;

            return required.EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .All(value => arguments.TryGetProperty(value!, out var argument)
                    && argument.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    && (argument.ValueKind != JsonValueKind.String
                        || !string.IsNullOrWhiteSpace(argument.GetString())));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadPath(JsonElement data, string path)
    {
        var current = data;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
                return null;
        }
        return ElementText(current);
    }

    private static IReadOnlyDictionary<string, object?> ReadPresentationData(JsonElement data, string? path)
    {
        var selected = data;
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (selected.ValueKind != JsonValueKind.Object
                    || !selected.TryGetProperty(segment, out selected))
                    return new Dictionary<string, object?>();
            }
        }
        return selected.ValueKind == JsonValueKind.Object
            ? selected.EnumerateObject().ToDictionary(value => value.Name, value => (object?)value.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>();
    }

    private static string? ElementText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText()
    };

    private static FragmentRenderMode ParseMode(string? value) =>
        Enum.TryParse<FragmentRenderMode>(value, true, out var parsed) ? parsed : FragmentRenderMode.Inline;

    private static FragmentPriority ParsePriority(string? value) =>
        Enum.TryParse<FragmentPriority>(value, true, out var parsed) ? parsed : FragmentPriority.Optional;

    private static string StableHash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..16];
}

public sealed class StageConditionEvaluator
{
    public bool Evaluate(
        StageConditionDefinition? condition,
        DeterministicStageExecutionContext context,
        string? requiredSignal = null)
    {
        if (!string.IsNullOrWhiteSpace(requiredSignal)
            && !context.Signals.Any(value => value.Type.Equals(requiredSignal, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (condition is null)
            return true;
        if (condition.All.Count > 0 && !condition.All.All(value => Evaluate(value, context)))
            return false;
        if (condition.Any.Count > 0 && !condition.Any.Any(value => Evaluate(value, context)))
            return false;
        if (condition.Not is not null && Evaluate(condition.Not, context))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.FactPresent) && IsMissing(context.Facts, condition.FactPresent))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.FactMissing) && !IsMissing(context.Facts, condition.FactMissing))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.FactChanged) && !context.ChangedFacts.Contains(condition.FactChanged))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.SignalPresent)
            && !context.Signals.Any(value => value.Type.Equals(condition.SignalPresent, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.VerificationActive)
            && !context.ActiveVerifications.Contains(condition.VerificationActive))
            return false;
        if (!string.IsNullOrWhiteSpace(condition.VerificationMissing)
            && context.ActiveVerifications.Contains(condition.VerificationMissing))
            return false;
        if (condition.FactEquals is { } equality)
        {
            context.Facts.TryGetValue(equality.Key, out var actual);
            var expected = equality.Value.ValueKind == JsonValueKind.String
                ? equality.Value.GetString()
                : equality.Value.GetRawText();
            if (!string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool IsMissing(IReadOnlyDictionary<string, string> facts, string key) =>
        !facts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value);
}

public sealed partial class OperationArgumentBinder
{
    [GeneratedRegex("^\\{\\{fact\\.([a-zA-Z0-9_.-]+)\\}\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactFactPattern();

    [GeneratedRegex("^\\{\\{signal\\.([a-zA-Z0-9_.-]+)\\.value\\}\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactSignalValuePattern();

    [GeneratedRegex("^\\{\\{signal\\.([a-zA-Z0-9_.-]+)\\.value\\.([a-zA-Z0-9_.-]+)\\}\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactSignalValuePathPattern();

    public JsonElement Bind(
        IReadOnlyDictionary<string, JsonElement> templates,
        DeterministicStageExecutionContext context)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, template) in templates)
        {
            var value = BindValue(template, context);
            if (value is not MissingValue)
                values[key] = value;
        }
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return document.RootElement.Clone();
    }

    private static object? BindValue(JsonElement template, DeterministicStageExecutionContext context)
    {
        if (template.ValueKind != JsonValueKind.String)
            return template.Clone();

        var raw = template.GetString() ?? string.Empty;
        if (raw.Equals("{{turn.message}}", StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(context.LatestUserMessage) ? MissingValue.Instance : context.LatestUserMessage;
        var factMatch = ExactFactPattern().Match(raw);
        if (factMatch.Success)
        {
            return context.Facts.TryGetValue(factMatch.Groups[1].Value, out var factValue)
                && !string.IsNullOrWhiteSpace(factValue)
                    ? factValue
                    : MissingValue.Instance;
        }

        var signalPathMatch = ExactSignalValuePathPattern().Match(raw);
        if (signalPathMatch.Success)
        {
            var pathSignal = context.Signals.LastOrDefault(value =>
                value.Type.Equals(signalPathMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            if (pathSignal is null)
                return MissingValue.Instance;
            var selected = pathSignal.Value;
            foreach (var segment in signalPathMatch.Groups[2].Value.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (selected.ValueKind != JsonValueKind.Object || !selected.TryGetProperty(segment, out selected))
                    return MissingValue.Instance;
            }
            return ElementValue(selected);
        }

        var signalMatch = ExactSignalValuePattern().Match(raw);
        if (!signalMatch.Success)
            return raw;

        var signal = context.Signals.LastOrDefault(value =>
            value.Type.Equals(signalMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        return signal is null ? MissingValue.Instance : ElementValue(signal.Value);
    }
    private static object? ElementValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.Clone()
    };
    private sealed class MissingValue
    {
        public static MissingValue Instance { get; } = new();
    }
}
