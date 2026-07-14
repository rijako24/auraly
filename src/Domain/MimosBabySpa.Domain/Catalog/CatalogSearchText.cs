using System.Globalization;
using System.Text;

namespace MimosBabySpa.Domain.Catalog;

public static class CatalogSearchText
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "al",
        "con",
        "de",
        "del",
        "el",
        "en",
        "la",
        "las",
        "los",
        "o",
        "para",
        "por",
        "un",
        "una",
        "y"
    };

    public static IReadOnlyList<string> GetSearchTerms(string? value)
    {
        var normalized = NormalizeForTokenSearch(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => !StopWords.Contains(term))
            .Where(term => term.Length > 1 || term.Any(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> GetFallbackQueries(string? value, int maxCount = 8)
    {
        var terms = GetSearchTerms(value);
        if (terms.Count == 0 || maxCount <= 0)
            return [];

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var original = NormalizeCompact(value);

        void Add(string candidate)
        {
            var trimmed = candidate.Trim();
            var key = NormalizeCompact(trimmed);
            if (trimmed.Length > 0
                && key.Length > 0
                && key != original
                && seen.Add(key)
                && results.Count < maxCount)
            {
                results.Add(trimmed);
            }
        }

        for (var index = 0; index < terms.Count && results.Count < maxCount; index++)
        {
            foreach (var singular in GetSingularFallbacks(terms[index]))
            {
                var candidate = terms.ToArray();
                candidate[index] = singular;
                Add(string.Join(' ', candidate));
            }
        }

        foreach (var term in terms)
        {
            Add(term);
            foreach (var singular in GetSingularFallbacks(term))
                Add(singular);
        }

        return results;
    }

    private static IEnumerable<string> GetSingularFallbacks(string term)
    {
        if (term.Length <= 3 || !term.All(char.IsLetter) || !term.EndsWith('s'))
            yield break;

        if (term.Length > 4 && term.EndsWith("ces", StringComparison.Ordinal))
            yield return $"{term[..^3]}z";

        var withoutS = term[..^1];
        if (withoutS.Length >= 3)
            yield return withoutS;

        if (term.Length > 4 && term.EndsWith("es", StringComparison.Ordinal))
        {
            var withoutEs = term[..^2];
            if (withoutEs.Length >= 3 && !withoutEs.Equals(withoutS, StringComparison.Ordinal))
                yield return withoutEs;
        }
    }

    public static bool ContainsAllTerms(string? query, params string?[] values)
    {
        var terms = GetSearchTerms(query);
        if (terms.Count == 0)
            return true;

        var searchable = NormalizeForTokenSearch(string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v))));
        return terms.All(term => searchable.Contains(term, StringComparison.Ordinal));
    }

    public static string NormalizeCompact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string NormalizeForTokenSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return builder.ToString();
    }
}
