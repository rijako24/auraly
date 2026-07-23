using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Planning;

public static class OptionSelectorResolver
{
    public static TurnPlan Resolve(
        TurnPlan plan,
        TurnPlanScope scope,
        string latestUserMessage,
        IReadOnlyList<ChatMessage> recentConversation,
        out OptionSelectorReference? resolvedReference)
    {
        resolvedReference = null;
        var references = OptionSelectorReferenceDetector.Find(scope, latestUserMessage);
        if (references.Count != 1)
            return plan;

        var reference = references[0];
        if (!WasPresented(reference, recentConversation))
            return MarkAmbiguous(plan, reference.Fact.Key);

        resolvedReference = reference;
        var facts = plan.Facts
            .Where(claim => !claim.Key.Equals(
                reference.Fact.Key,
                StringComparison.OrdinalIgnoreCase))
            .Append(new PlannedFactClaim
            {
                Key = reference.Fact.Key,
                Operation = TurnPlanOperations.Set,
                Value = JsonSerializer.SerializeToElement(reference.Option.Value),
                Evidence = reference.Option.Selector!,
                Confidence = 1
            })
            .ToArray();

        var ambiguousFields = plan.Response.AmbiguousFields
            .Where(field => !field.Equals(
                reference.Fact.Key,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = ambiguousFields.Length == 0
                    && plan.Response.Mode.Equals("ask_clarification", StringComparison.OrdinalIgnoreCase)
                        ? "continue"
                        : plan.Response.Mode,
                AmbiguousFields = ambiguousFields
            }
        };
    }

    public static OptionSelectorReference? FindPresentedReference(
        TurnPlanScope scope,
        string latestUserMessage,
        IReadOnlyList<ChatMessage> recentConversation)
    {
        var references = OptionSelectorReferenceDetector.Find(scope, latestUserMessage);
        if (references.Count != 1)
            return null;

        var reference = references[0];
        return WasPresented(reference, recentConversation)
            ? reference
            : null;
    }

    private static bool WasPresented(
        OptionSelectorReference reference,
        IReadOnlyList<ChatMessage> recentConversation)
    {
        var previousMessage = recentConversation.LastOrDefault(message =>
            !string.IsNullOrWhiteSpace(message.Content));
        return previousMessage?.Role == ChatRole.Assistant
            && OptionsWerePresented(
                reference.Fact,
                reference.Option,
                previousMessage.Content!);
    }

    private static TurnPlan MarkAmbiguous(TurnPlan plan, string factKey)
    {
        var ambiguousFields = plan.Response.AmbiguousFields
            .Append(factKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts
                .Where(claim => !claim.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification",
                AmbiguousFields = ambiguousFields
            }
        };
    }

    private static bool OptionsWerePresented(
        FactSchemaEntry fact,
        FactValueOption selectedOption,
        string assistantMessage)
    {
        var presented = fact.Options
            .Where(option => OptionWasPresented(option, assistantMessage))
            .ToArray();
        var minimumOptionCount = Math.Min(2, fact.Options.Count);

        return presented.Length >= minimumOptionCount
            && presented.Contains(selectedOption);
    }

    private static bool OptionWasPresented(FactValueOption option, string assistantMessage)
    {
        if (string.IsNullOrWhiteSpace(option.Selector))
            return false;

        var selector = Normalize(option.Selector);
        var label = Normalize(option.Label);
        var value = Normalize(option.Value);
        return assistantMessage
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Any(line =>
            {
                var optionPrefix = $@"^(?:[*-]\s*)?{Regex.Escape(selector)}(?:\s*[\)\].:\-]\s*|\s+)";
                return Regex.IsMatch(line, optionPrefix, RegexOptions.CultureInvariant)
                    && (line.Contains(label, StringComparison.Ordinal)
                        || line.Contains(value, StringComparison.Ordinal));
            });
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
