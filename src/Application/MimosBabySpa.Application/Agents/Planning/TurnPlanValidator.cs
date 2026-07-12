using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed class TurnPlanValidator
{
    public TurnPlanValidationResult Validate(TurnPlan plan, TurnPlanScope scope, string latestUserMessage)
    {
        var issues = new List<TurnPlanValidationIssue>();
        ValidateFlowIntent(plan, scope, latestUserMessage, issues);
        ValidateFacts(plan, scope, latestUserMessage, issues);
        ValidateOptionSelectorCoverage(plan, scope, latestUserMessage, issues);
        ValidateSignals(plan, scope, latestUserMessage, issues);
        ValidateResponseDirective(plan, scope, issues);

        if (plan.Decision is not null && !EvidenceIsSupported(latestUserMessage, plan.Decision.Evidence))
            Add(issues, "decision.evidence_unsupported",
                "Customer decision evidence is not supported by the latest user message.",
                TurnPlanIssueTarget.Decision, recovery: TurnPlanRecoveryAction.DropTarget);

        return new TurnPlanValidationResult(issues);
    }

    private static void ValidateFlowIntent(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<TurnPlanValidationIssue> issues)
    {
        if (scope.Flows.Count > 0 && !scope.Flows.ContainsKey(plan.FlowIntent.CandidateFlow))
            Add(issues, "flow.outside_scope",
                $"Flow '{plan.FlowIntent.CandidateFlow}' is outside the configured flow scope.",
                TurnPlanIssueTarget.FlowIntent);

        if (plan.FlowIntent.Confidence is < 0 or > 1)
            Add(issues, "flow.confidence_invalid",
                "Flow intent confidence must be between 0 and 1.",
                TurnPlanIssueTarget.FlowIntent);

        if (!string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence)
            && !EvidenceIsSupported(message, plan.FlowIntent.Evidence))
            Add(issues, "flow.evidence_unsupported",
                "Flow intent evidence is not supported by the latest user message.",
                TurnPlanIssueTarget.FlowIntent,
                recovery: TurnPlanRecoveryAction.FallbackToPrimaryFlow);

        var isPrimaryFallback = plan.FlowIntent.CandidateFlow.Equals(
            scope.PrimaryFlowId, StringComparison.OrdinalIgnoreCase);
        if (scope.Flows.Count > 1
            && !isPrimaryFallback
            && string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence))
            Add(issues, "flow.evidence_required",
                "A non-primary flow intent requires evidence from the latest user message.",
                TurnPlanIssueTarget.FlowIntent,
                recovery: TurnPlanRecoveryAction.FallbackToPrimaryFlow);
    }

    private static void ValidateResponseDirective(
        TurnPlan plan,
        TurnPlanScope scope,
        ICollection<TurnPlanValidationIssue> issues)
    {
        if (plan.Response.Mode is not ("continue" or "ask_clarification"))
            Add(issues, "response.mode_unsupported",
                $"Unsupported response mode '{plan.Response.Mode}'.",
                TurnPlanIssueTarget.Response);

        var allowed = scope.Facts.Keys
            .Concat(scope.Signals.Keys)
            .Concat(["flowIntent", "decision"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in plan.Response.AmbiguousFields)
        {
            if (!ambiguous.Add(field))
                Add(issues, "response.ambiguous_duplicate",
                    $"Ambiguous field '{field}' appears more than once.",
                    TurnPlanIssueTarget.Response, field);
            if (!allowed.Contains(field))
                Add(issues, "response.ambiguous_outside_scope",
                    $"Ambiguous field '{field}' is outside the current planner scope.",
                    TurnPlanIssueTarget.Response, field);
        }

        var asksClarification = plan.Response.Mode.Equals(
            "ask_clarification", StringComparison.OrdinalIgnoreCase);
        if (asksClarification && ambiguous.Count == 0)
            Add(issues, "response.clarification_fields_required",
                "ask_clarification requires at least one ambiguous field.",
                TurnPlanIssueTarget.Response);
        if (!asksClarification && ambiguous.Count > 0)
            Add(issues, "response.clarification_mode_required",
                "response.ambiguousFields must be empty unless response.mode is ask_clarification.",
                TurnPlanIssueTarget.Response);

        foreach (var fact in plan.Facts.Where(fact => ambiguous.Contains(fact.Key)))
            Add(issues, "fact.mutates_ambiguous",
                $"Fact '{fact.Key}' cannot be mutated while it is ambiguous.",
                TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);
        foreach (var signal in plan.Signals.Where(signal => ambiguous.Contains(signal.Type)))
            Add(issues, "signal.emits_ambiguous",
                $"Signal '{signal.Type}' cannot be emitted while it is ambiguous.",
                TurnPlanIssueTarget.Signal, signal.Type, TurnPlanRecoveryAction.DropTarget);
        if (ambiguous.Contains("decision") && plan.Decision is not null)
            Add(issues, "decision.emits_ambiguous",
                "Customer decision cannot be emitted while it is ambiguous.",
                TurnPlanIssueTarget.Decision, recovery: TurnPlanRecoveryAction.DropTarget);
    }

    private static void ValidateFacts(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<TurnPlanValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in plan.Facts)
        {
            if (!seen.Add(fact.Key))
                Add(issues, "fact.duplicate",
                    $"Fact '{fact.Key}' appears more than once in the same plan.",
                    TurnPlanIssueTarget.Fact, fact.Key);

            if (!scope.Facts.TryGetValue(fact.Key, out var definition))
            {
                Add(issues, "fact.outside_scope",
                    $"Fact '{fact.Key}' is outside the current planner scope.",
                    TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);
                continue;
            }

            if (fact.Operation is not (TurnPlanOperations.Set or TurnPlanOperations.Clear))
                Add(issues, "fact.operation_unsupported",
                    $"Fact '{fact.Key}' has unsupported operation '{fact.Operation}'.",
                    TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);

            if (!EvidenceIsSupported(message, fact.Evidence))
                Add(issues, "fact.evidence_unsupported",
                    $"Fact '{fact.Key}' evidence is not supported by the latest user message.",
                    TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);

            if (fact.Confidence is < 0 or > 1)
                Add(issues, "fact.confidence_invalid",
                    $"Fact '{fact.Key}' confidence must be between 0 and 1.",
                    TurnPlanIssueTarget.Fact, fact.Key);


            if (fact.Operation == TurnPlanOperations.Set
                && !ValueMatchesType(fact.Value, definition.Type))
                Add(issues, "fact.type_mismatch",
                    $"Fact '{fact.Key}' value does not match configured type '{definition.Type}'.",
                    TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);

            if (fact.Operation == TurnPlanOperations.Set
                && definition.Options.Count > 0
                && (fact.Value.ValueKind != JsonValueKind.String
                    || !definition.Options.Any(option => option.Value.Equals(
                        fact.Value.GetString() ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))))
                Add(issues, "fact.canonical_value_invalid",
                    $"Fact '{fact.Key}' value is outside its configured canonical values.",
                    TurnPlanIssueTarget.Fact, fact.Key, TurnPlanRecoveryAction.DropTarget);
        }
    }

    private static void ValidateOptionSelectorCoverage(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<TurnPlanValidationIssue> issues)
    {
        var matches = OptionSelectorReferenceDetector.Find(scope, message);
        if (matches.Count != 1)
            return;

        var match = matches[0];
        if (plan.Facts.Any(fact => fact.Key.Equals(
                match.Fact.Key, StringComparison.OrdinalIgnoreCase))
            || plan.Response.AmbiguousFields.Contains(
                match.Fact.Key, StringComparer.OrdinalIgnoreCase))
            return;

        Add(issues, "fact.selector_missing_claim",
            $"Fact '{match.Fact.Key}' references configured selector '{match.Option.Selector}' but has no canonical claim.",
            TurnPlanIssueTarget.Fact,
            match.Fact.Key,
            TurnPlanRecoveryAction.ClarifyTarget);
    }

    private static void ValidateSignals(
        TurnPlan plan,
        TurnPlanScope scope,
        string message,
        ICollection<TurnPlanValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in plan.Signals)
        {
            if (!seen.Add(signal.Type))
                Add(issues, "signal.duplicate",
                    $"Signal '{signal.Type}' appears more than once in the same plan.",
                    TurnPlanIssueTarget.Signal, signal.Type);

            if (!scope.Signals.TryGetValue(signal.Type, out var definition))
            {
                Add(issues, "signal.outside_scope",
                    $"Signal '{signal.Type}' is outside the current planner scope.",
                    TurnPlanIssueTarget.Signal, signal.Type, TurnPlanRecoveryAction.DropTarget);
                continue;
            }

            if (definition.ValueSchema.ValueKind != JsonValueKind.Object
                || !JsonSchemaValueValidator.IsValid(signal.Value, definition.ValueSchema))
                Add(issues, "signal.schema_invalid",
                    $"Signal '{signal.Type}' value does not match its configured JSON Schema.",
                    TurnPlanIssueTarget.Signal, signal.Type, TurnPlanRecoveryAction.DropTarget);

            if (!EvidenceIsSupported(message, signal.Evidence))
                Add(issues, "signal.evidence_unsupported",
                    $"Signal '{signal.Type}' evidence is not supported by the latest user message.",
                    TurnPlanIssueTarget.Signal, signal.Type, TurnPlanRecoveryAction.DropTarget);

            if (signal.Confidence is < 0 or > 1)
                Add(issues, "signal.confidence_invalid",
                    $"Signal '{signal.Type}' confidence must be between 0 and 1.",
                    TurnPlanIssueTarget.Signal, signal.Type);


        }
    }

    private static void Add(
        ICollection<TurnPlanValidationIssue> issues,
        string code,
        string message,
        TurnPlanIssueTarget target,
        string? field = null,
        TurnPlanRecoveryAction recovery = TurnPlanRecoveryAction.None) =>
        issues.Add(new TurnPlanValidationIssue(code, message, target, field, recovery));

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
                && DateOnly.TryParseExact(
                    raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
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
        if (string.IsNullOrWhiteSpace(normalizedMessage) || string.IsNullOrWhiteSpace(normalizedEvidence))
            return false;
        if ($" {normalizedMessage} ".Contains($" {normalizedEvidence} ", StringComparison.Ordinal))
            return true;

        var messageTokens = normalizedMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var evidenceTokens = normalizedEvidence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (evidenceTokens.Length < 2 || evidenceTokens.Length > messageTokens.Length)
            return false;

        for (var start = 0; start <= messageTokens.Length - evidenceTokens.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < evidenceTokens.Length; offset++)
            {
                if (TokensMatch(messageTokens[start + offset], evidenceTokens[offset]))
                    continue;
                matches = false;
                break;
            }
            if (matches)
                return true;
        }
        return false;
    }

    private static bool TokensMatch(string left, string right) =>
        left.Equals(right, StringComparison.Ordinal)
        || Math.Min(left.Length, right.Length) >= 4
        && (left.StartsWith(right, StringComparison.Ordinal)
            || right.StartsWith(left, StringComparison.Ordinal));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return Regex.Replace(
            new string(chars).Normalize(NormalizationForm.FormC), "\\s+", " ").Trim();
    }
}