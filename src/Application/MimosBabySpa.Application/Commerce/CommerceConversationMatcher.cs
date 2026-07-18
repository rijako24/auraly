using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Commerce;

internal static class CommerceConversationMatcher
{
    public static bool IsExactPhrase(string? message, IEnumerable<string>? phrases)
    {
        var normalized = ProductSearchText.NormalizeWords(message ?? string.Empty);
        return normalized.Length > 0 && (phrases ?? []).Any(phrase =>
            normalized.Equals(ProductSearchText.NormalizeWords(phrase), StringComparison.Ordinal));
    }

    public static bool Matches(string? message, IEnumerable<CommercePhraseRule>? rules)
    {
        var normalized = ProductSearchText.NormalizeWords(message ?? string.Empty);
        if (normalized.Length == 0)
            return false;

        return (rules ?? []).Any(rule =>
        {
            var phrase = ProductSearchText.NormalizeWords(rule.Phrase);
            if (phrase.Length == 0)
                return false;
            return rule.Match.ToLowerInvariant() switch
            {
                CommercePhraseMatchModes.Contains =>
                    $" {normalized} ".Contains($" {phrase} ", StringComparison.Ordinal),
                CommercePhraseMatchModes.Prefix =>
                    normalized.Equals(phrase, StringComparison.Ordinal)
                    || normalized.StartsWith($"{phrase} ", StringComparison.Ordinal),
                CommercePhraseMatchModes.Suffix =>
                    normalized.Equals(phrase, StringComparison.Ordinal)
                    || normalized.EndsWith($" {phrase}", StringComparison.Ordinal),
                _ => normalized.Equals(phrase, StringComparison.Ordinal)
            };
        });
    }
    public static bool ContainsPhrase(string? message, IEnumerable<string>? phrases)
    {
        var normalized = $" {ProductSearchText.NormalizeWords(message ?? string.Empty)} ";
        return (phrases ?? []).Any(phrase =>
        {
            var candidate = ProductSearchText.NormalizeWords(phrase);
            return candidate.Length > 0
                && normalized.Contains($" {candidate} ", StringComparison.Ordinal);
        });
    }

    public static IReadOnlyList<string> SplitClauses(
        string message,
        IEnumerable<string>? separators)
    {
        var normalized = $" {ProductSearchText.NormalizeWords(message)} ";
        foreach (var separator in separators ?? [])
        {
            var candidate = ProductSearchText.NormalizeWords(separator);
            if (candidate.Length > 0)
                normalized = normalized.Replace($" {candidate} ", " | ", StringComparison.Ordinal);
        }

        return normalized
            .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
