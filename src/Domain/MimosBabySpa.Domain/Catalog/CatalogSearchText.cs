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
