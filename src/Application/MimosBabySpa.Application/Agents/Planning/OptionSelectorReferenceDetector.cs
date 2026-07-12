using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record OptionSelectorReference(FactSchemaEntry Fact, FactValueOption Option);

public static class OptionSelectorReferenceDetector
{
    public static IReadOnlyList<OptionSelectorReference> Find(TurnPlanScope scope, string message) =>
        scope.Facts.Values
            .SelectMany(fact => fact.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Selector)
                    && Appears(message, option.Selector!))
                .Select(option => new OptionSelectorReference(fact, option)))
            .ToList();

    private static bool Appears(string message, string selector)
    {
        var trimmedSelector = selector.Trim();
        if (trimmedSelector.Length == 1 && char.IsLetterOrDigit(trimmedSelector[0]))
        {
            var normalizedMessage = Regex.Replace(message.Trim(), @"[^\p{L}\p{N}]+", " ").Trim();
            var explicitSelectionPattern = $@"^(?:"
                + @"(?:opcion|opci?n|alternativa)\s+|"
                + @"(?:la|elijo|escojo|prefiero)\s+|"
                + @"quiero\s+(?:la\s+)?(?:(?:opcion|opci?n)\s+)?"
                + $@")?{Regex.Escape(trimmedSelector)}(?:\s+por\s+favor)?$";
            return Regex.IsMatch(
                normalizedMessage,
                explicitSelectionPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(trimmedSelector)}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}