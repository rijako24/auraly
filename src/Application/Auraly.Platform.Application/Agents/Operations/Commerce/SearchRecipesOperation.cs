using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

using Auraly.Platform.Application.Agents.Operations.Support;
namespace Auraly.Platform.Application.Agents.Operations.Commerce;

public sealed partial class SearchRecipesOperation : IAgentOperation
{
    private static readonly HttpClient HttpClient = new();
    private readonly ILogger<SearchRecipesOperation> _logger;

    public SearchRecipesOperation(ILogger<SearchRecipesOperation> logger)
    {
        _logger = logger;
    }

    private const string InputSchema = """
        {
          "type": "object",
          "properties": {
            "ingredient": {
              "type": "string",
              "description": "Main product or ingredient to search recipes for, for example pechuga, arroz, pasta or carne molida."
            },
            "query": {
              "type": "string",
              "description": "Optional extra search terms such as facil, rapida, al horno, colombiana."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 5
            }
          },
          "required": ["ingredient"]
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.search_recipes", InputSchema,
        ["recipes.found", "missing_prerequisites", "recipe_search_failed", "recipe_search_timeout"],
        [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement arguments,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!OperationJsonHelper.TryGetString(arguments, "ingredient", out var ingredient))
            return OperationOutcome.Fail("missing_prerequisites", "ingredient is required.", true);

        var extraQuery = OperationJsonHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var limit = OperationJsonHelper.TryGetInt(arguments, "limit", out var l) ? Math.Clamp(l, 1, 5) : 3;
        var searchQuery = BuildSearchQuery(ingredient, extraQuery);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildDuckDuckGoUri(searchQuery));
            request.Headers.UserAgent.ParseAdd("TalkioAI/1.0 (+https://talkio.ai)");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return OperationOutcome.Fail(
                    "recipe_search_failed",
                    $"Recipe search failed with HTTP {(int)response.StatusCode}.",
                    true);
            }

            var html = await response.Content.ReadAsStringAsync(timeout.Token);
            var results = ParseDuckDuckGoResults(html, limit);
            var catalogSearchQueries = BuildCatalogSearchQueries(ingredient, results);

            return OperationOutcome.Ok("recipes.found", new
            {
                query = searchQuery,
                source = "duckduckgo_html",
                count = results.Count,
                results,
                catalog_search_queries = catalogSearchQueries,
                usage_guidance = "Presenta maximo dos ideas breves y conserva los enlaces/fuentes devueltos. Para vender ingredientes, usa catalog_search_queries con search_products; no concluyas disponibilidad de catalogo desde la receta."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationOutcome.Fail("recipe_search_timeout", "Recipe search timed out.", true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recipe search failed for {Query}", searchQuery);
            return OperationOutcome.Fail("recipe_search_failed", "Recipe search failed.", true);
        }
    }

    private static string BuildSearchQuery(string ingredient, string? extraQuery)
    {
        var terms = string.IsNullOrWhiteSpace(extraQuery)
            ? ingredient.Trim()
            : $"{ingredient.Trim()} {extraQuery.Trim()}";

        return $"receta {terms} facil";
    }

    private static Uri BuildDuckDuckGoUri(string query) =>
        new($"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kl=es-es");

    private static IReadOnlyList<WebRecipeSearchResult> ParseDuckDuckGoResults(string html, int limit)
    {
        var results = new List<WebRecipeSearchResult>();
        foreach (Match match in ResultRegex().Matches(html))
        {
            var rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value);
            var title = Clean(match.Groups["title"].Value);
            if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(title))
                continue;

            var url = NormalizeDuckDuckGoUrl(rawUrl);
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var snippet = ExtractSnippet(html, match.Index);
            if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new WebRecipeSearchResult(title, url, snippet));
            if (results.Count >= limit)
                break;
        }

        return results;
    }

    private static string? NormalizeDuckDuckGoUrl(string rawUrl)
    {
        if (rawUrl.StartsWith("//", StringComparison.Ordinal))
            rawUrl = "https:" + rawUrl;

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            && uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2 && pieces[0].Equals("uddg", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pieces[1]);
            }
        }

        return Uri.TryCreate(rawUrl, UriKind.Absolute, out _) ? rawUrl : null;
    }

    private static string? ExtractSnippet(string html, int startIndex)
    {
        var endIndex = Math.Min(html.Length, startIndex + 3000);
        var fragment = html[startIndex..endIndex];
        var match = SnippetRegex().Match(fragment);
        return match.Success ? Clean(match.Groups["snippet"].Value) : null;
    }

    private static string Clean(string value)
    {
        var decoded = WebUtility.HtmlDecode(TagRegex().Replace(value, " "));
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }
    private static IReadOnlyList<string> BuildCatalogSearchQueries(
        string ingredient,
        IReadOnlyList<WebRecipeSearchResult> results)
    {
        var queries = new List<string>();
        AddCatalogQueryCandidates(queries, ingredient);

        foreach (var result in results)
        {
            AddCatalogQueryCandidates(queries, result.Title);
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                AddCatalogQueryCandidates(queries, result.Snippet);
        }

        return queries
            .Where(query => query.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static void AddCatalogQueryCandidates(List<string> queries, string text)
    {
        var tokens = TokenizeCatalogQuery(text).ToArray();
        if (tokens.Length == 0)
            return;

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            AddQuery(queries, $"{tokens[i]} {tokens[i + 1]}");
            AddQuery(queries, $"{Singularize(tokens[i])} {Singularize(tokens[i + 1])}");
        }

        foreach (var token in tokens)
        {
            AddQuery(queries, token);
            AddQuery(queries, Singularize(token));
        }
    }

    private static IEnumerable<string> TokenizeCatalogQuery(string text)
    {
        var normalized = RemoveDiacritics(text).ToLowerInvariant();
        foreach (var token in Regex.Split(normalized, @"[^a-z0-9]+"))
        {
            if (token.Length < 3 || CatalogQueryStopWords.Contains(token))
                continue;

            yield return token;
        }
    }

    private static string Singularize(string token)
    {
        if (token.Length > 4 && token.EndsWith("es", StringComparison.Ordinal))
            return token[..^2];

        if (token.Length > 3 && token.EndsWith('s'))
            return token[..^1];

        return token;
    }

    private static void AddQuery(List<string> queries, string query)
    {
        query = WhitespaceRegex().Replace(query, " ").Trim();
        if (query.Length >= 3)
            queries.Add(query);
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static readonly HashSet<string> CatalogQueryStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "quiero", "preparar", "prepara", "preparacion", "receta", "recetas",
        "facil", "faciles", "cocinar", "cocino", "hacer", "como", "para",
        "con", "sin", "una", "uno", "unos", "unas", "del", "las", "los",
        "que", "por", "esta", "este", "estas", "estos", "tipo", "estilo",
        "casera", "casero", "deliciosa", "delicioso", "ideas", "paso",
        "pasos", "ingrediente", "ingredientes", "rapida", "rapido"
    };

    [GeneratedRegex("<a[^>]+class=\"result__a\"[^>]+href=\"(?<url>[^\"]+)\"[^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ResultRegex();

    [GeneratedRegex("<a[^>]+class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SnippetRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record WebRecipeSearchResult(string Title, string Url, string? Snippet);
}
