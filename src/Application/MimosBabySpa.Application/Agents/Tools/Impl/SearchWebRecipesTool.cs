using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("search_web_recipes")]
public sealed partial class SearchWebRecipesTool : IAgentTool
{
    private static readonly HttpClient HttpClient = new();
    private readonly ILogger<SearchWebRecipesTool> _logger;

    public SearchWebRecipesTool(ILogger<SearchWebRecipesTool> logger)
    {
        _logger = logger;
    }

    public string Name => "search_web_recipes";

    public string Description =>
        "Searches the public web for recipe pages related to a product or ingredient. " +
        "Use results as external references; do not claim the business owns the recipes.";

    public string ParametersSchema => """
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

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "ingredient", out var ingredient))
            return ToolResultHelper.MissingPrerequisites(["ingredient"]);

        var extraQuery = ToolResultHelper.TryGetString(arguments, "query", out var q) ? q : null;
        var limit = ToolResultHelper.TryGetInt(arguments, "limit", out var l) ? Math.Clamp(l, 1, 5) : 3;
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
                return ToolResultHelper.Error(
                    "recipe_search_failed",
                    $"Recipe search failed with HTTP {(int)response.StatusCode}.",
                    recoverable: true);
            }

            var html = await response.Content.ReadAsStringAsync(timeout.Token);
            var results = ParseDuckDuckGoResults(html, limit);

            return ToolResultHelper.Ok(new
            {
                query = searchQuery,
                source = "duckduckgo_html",
                count = results.Count,
                results,
                usage_guidance = "Presenta maximo dos ideas breves y conserva los enlaces/fuentes devueltos. Si no hay detalle suficiente, invita a abrir el enlace o pide permiso para buscar otra receta."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResultHelper.Error("recipe_search_timeout", "Recipe search timed out.", recoverable: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recipe search failed for {Query}", searchQuery);
            return ToolResultHelper.Error("recipe_search_failed", "Recipe search failed.", recoverable: true);
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
