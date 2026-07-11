using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record TurnPlanValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class TurnPlanValidator
{
    public TurnPlanValidationResult Validate(TurnPlan plan, TurnPlanScope scope, string latestUserMessage)
    {
        var errors = new List<string>();
        ValidateFlowIntent(plan, scope, latestUserMessage, errors);
        ValidateFacts(plan, scope, latestUserMessage, errors);
        ValidateSignals(plan, scope, latestUserMessage, errors);
        ValidateResponseDirective(plan, scope, errors);

        if (plan.Decision is not null && !EvidenceIsSupported(latestUserMessage, plan.Decision.Evidence))
            errors.Add("Customer decision evidence is not supported by the latest user message.");

        return new TurnPlanValidationResult(errors);
    }

    private static void ValidateFlowIntent(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<string> errors)
    {
        if (scope.Flows.Count > 0 && !scope.Flows.ContainsKey(plan.FlowIntent.CandidateFlow))
            errors.Add($"Flow '{plan.FlowIntent.CandidateFlow}' is outside the configured flow scope.");

        if (plan.FlowIntent.Confidence is < 0 or > 1)
            errors.Add("Flow intent confidence must be between 0 and 1.");

        if (!string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence)
            && !EvidenceIsSupported(message, plan.FlowIntent.Evidence))
        {
            errors.Add("Flow intent evidence is not supported by the latest user message.");
        }

        var isPrimaryFallback = plan.FlowIntent.CandidateFlow.Equals(
            scope.PrimaryFlowId,
            StringComparison.OrdinalIgnoreCase);

        if (scope.Flows.Count > 1
            && !isPrimaryFallback
            && string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence))
        {
            errors.Add("A non-primary flow intent requires evidence from the latest user message.");
        }
    }
    private static void ValidateResponseDirective(
        TurnPlan plan,
        TurnPlanScope scope,
        ICollection<string> errors)
    {
        if (plan.Response.Mode is not ("continue" or "ask_clarification"))
            errors.Add($"Unsupported response mode '{plan.Response.Mode}'.");

        var allowed = scope.Facts.Keys
            .Concat(scope.Signals.Keys)
            .Concat(["flowIntent", "decision"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in plan.Response.AmbiguousFields)
        {
            if (!ambiguous.Add(field))
                errors.Add($"Ambiguous field '{field}' appears more than once.");
            if (!allowed.Contains(field))
                errors.Add($"Ambiguous field '{field}' is outside the current planner scope.");
        }

        var asksClarification = plan.Response.Mode.Equals("ask_clarification", StringComparison.OrdinalIgnoreCase);
        if (asksClarification && ambiguous.Count == 0)
            errors.Add("ask_clarification requires at least one ambiguous field.");
        if (!asksClarification && ambiguous.Count > 0)
            errors.Add("response.ambiguousFields must be empty unless response.mode is ask_clarification.");

        foreach (var fact in plan.Facts.Where(fact => ambiguous.Contains(fact.Key)))
            errors.Add($"Fact '{fact.Key}' cannot be mutated while it is ambiguous.");
        foreach (var signal in plan.Signals.Where(signal => ambiguous.Contains(signal.Type)))
            errors.Add($"Signal '{signal.Type}' cannot be emitted while it is ambiguous.");
        if (ambiguous.Contains("decision") && plan.Decision is not null)
            errors.Add("Customer decision cannot be emitted while it is ambiguous.");
    }
    private static void ValidateFacts(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in plan.Facts)
        {
            if (!seen.Add(fact.Key))
                errors.Add($"Fact '{fact.Key}' appears more than once in the same plan.");

            if (!scope.Facts.TryGetValue(fact.Key, out var definition))
            {
                errors.Add($"Fact '{fact.Key}' is outside the current planner scope.");
                continue;
            }

            if (fact.Operation is not (TurnPlanOperations.Set or TurnPlanOperations.Clear))
                errors.Add($"Fact '{fact.Key}' has unsupported operation '{fact.Operation}'.");

            if (!EvidenceIsSupported(message, fact.Evidence))
                errors.Add($"Fact '{fact.Key}' evidence is not supported by the latest user message.");

            if (fact.Operation == TurnPlanOperations.Set && !ValueMatchesType(fact.Value, definition.Type))
                errors.Add($"Fact '{fact.Key}' value does not match configured type '{definition.Type}'.");
        }
    }

    private static void ValidateSignals(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in plan.Signals)
        {
            if (!seen.Add(signal.Type))
                errors.Add($"Signal '{signal.Type}' appears more than once in the same plan.");

            if (!scope.Signals.TryGetValue(signal.Type, out var definition))
            {
                errors.Add($"Signal '{signal.Type}' is outside the current planner scope.");
                continue;
            }

            if (definition.ValueSchema.ValueKind != JsonValueKind.Object
                || !JsonSchemaValueValidator.IsValid(signal.Value, definition.ValueSchema))
                errors.Add($"Signal '{signal.Type}' value does not match its configured JSON Schema.");

            if (!EvidenceIsSupported(message, signal.Evidence))
                errors.Add($"Signal '{signal.Type}' evidence is not supported by the latest user message.");
        }
    }

    private static bool ValueMatchesType(JsonElement value, string configuredType)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();

        return configuredType.Trim().ToLowerInvariant() switch
        {
            "number" => value.ValueKind == JsonValueKind.Number
                || decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out _),
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                || bool.TryParse(raw, out _),
            "date" => value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "time" => value.ValueKind == JsonValueKind.String && TimeSpan.TryParse(raw, out _),
            "email" => value.ValueKind == JsonValueKind.String && raw.Contains('@', StringComparison.Ordinal),
            "phone" => value.ValueKind == JsonValueKind.String && raw.Count(char.IsDigit) >= 7,
            _ => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(raw)
        };
    }

    private static bool EvidenceIsSupported(string message, string evidence)
    {
        var normalizedMessage = Normalize(message);
        var normalizedEvidence = Normalize(evidence);
        return !string.IsNullOrWhiteSpace(normalizedMessage)
            && !string.IsNullOrWhiteSpace(normalizedEvidence)
            && $" {normalizedMessage} ".Contains($" {normalizedEvidence} ", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return Regex.Replace(new string(chars).Normalize(NormalizationForm.FormC), "\\s+", " ").Trim();
    }
}
