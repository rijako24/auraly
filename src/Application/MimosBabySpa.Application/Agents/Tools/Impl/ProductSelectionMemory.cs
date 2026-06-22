using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class ProductSelectionMemory
{
    private const int MaxStoredCandidates = 10;
    private const string LastSearchKey = "system.last_product_search";
    private const string SelectedProductKey = "system.selected_product";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static async Task<ProductCandidate?> RememberSearchAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        string? query,
        ProductSearchResult result,
        CancellationToken ct)
    {
        var candidates = result.Products
            .Take(MaxStoredCandidates)
            .Select((product, index) => ProductCandidate.From(product, index + 1))
            .ToList();

        if (candidates.Count > 0)
        {
            var search = new ProductSearchSnapshot(query, ctx.LatestUserMessage, candidates);
            await SetFactAsync(factsService, ctx, LastSearchKey, JsonSerializer.Serialize(search, JsonOptions), ct);
        }

        var selected = InferSingleCandidate(candidates, ctx.LatestUserMessage, query);
        if (selected is not null)
            await RememberSelectedAsync(factsService, ctx, selected, ct);
        else
            await ClearSelectedAsync(factsService, ctx, ct);

        return selected;
    }

    public static bool TryGetSelected(AgentToolContext ctx, out ProductCandidate candidate) =>
        TryReadCandidate(ctx, SelectedProductKey, out candidate);

    public static bool TryResolveFromLastSearch(
        AgentToolContext ctx,
        string? selector,
        bool allowIndex,
        decimal? quantityToIgnore,
        out ProductCandidate candidate,
        out IReadOnlyList<ProductCandidate> matches)
    {
        candidate = default!;
        matches = [];

        if (!TryGetLastSearch(ctx, out var search) || search.Products.Count == 0)
            return false;

        matches = FindMatches(search.Products, selector, allowIndex, quantityToIgnore);
        if (matches.Count != 1)
            return false;

        candidate = matches[0];
        return true;
    }

    public static bool TryGetLastSearch(AgentToolContext ctx, out ProductSearchSnapshot snapshot)
    {
        snapshot = default!;
        if (!ctx.Facts.TryGetValue(LastSearchKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<ProductSearchSnapshot>(raw, JsonOptions);
            if (parsed is null)
                return false;

            snapshot = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string BuildClarificationHint(IReadOnlyList<ProductCandidate> candidates)
    {
        if (candidates.Count == 0)
            return "Busca el producto en catalogo antes de agregarlo.";

        var options = candidates
            .Take(5)
            .Select(c => $"{c.Index}. {c.Name} ({FormatMoney(c.UnitPrice, c.Currency)})");

        return "Pregunta una aclaracion corta antes de agregar al carrito. Opciones: " + string.Join("; ", options) + ".";
    }

    public static async Task ClearSelectedAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        await factsService.ClearFieldsAsync(ctx.ConversationId, [SelectedProductKey], ct);
        ctx.Facts.Remove(SelectedProductKey);
    }

    public static async Task RememberSelectedAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        ProductCandidate candidate,
        CancellationToken ct) =>
        await SetFactAsync(factsService, ctx, SelectedProductKey, JsonSerializer.Serialize(candidate, JsonOptions), ct);

    private static async Task SetFactAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        string key,
        string value,
        CancellationToken ct)
    {
        await factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, key, value, rememberAcrossRequests: false, ct);
        ctx.Facts[key] = value;
    }

    private static ProductCandidate? InferSingleCandidate(
        IReadOnlyList<ProductCandidate> candidates,
        string? userMessage,
        string? query)
    {
        if (candidates.Count == 0)
            return null;
        if (candidates.Count == 1)
            return candidates[0];

        var selector = string.Join(' ', new[] { userMessage, query }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var matches = FindMatches(candidates, selector, allowIndex: false, quantityToIgnore: null);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<ProductCandidate> FindMatches(
        IReadOnlyList<ProductCandidate> candidates,
        string? selector,
        bool allowIndex,
        decimal? quantityToIgnore)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return [];

        var trimmed = selector.Trim();

        if (allowIndex
            && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedIndex))
        {
            var byIndex = candidates.Where(c => c.Index == selectedIndex).ToList();
            if (byIndex.Count > 0)
                return byIndex;
        }

        var exact = candidates.Where(c => IsExactIdentifierMatch(c, trimmed)).ToList();
        if (exact.Count > 0)
            return exact;

        var priceMatches = ExtractPriceCandidates(trimmed)
            .SelectMany(price => candidates.Where(c => c.UnitPrice == price))
            .Distinct()
            .ToList();
        if (priceMatches.Count > 0)
            return priceMatches;

        var terms = GetSelectionTerms(trimmed, quantityToIgnore);
        if (terms.Count == 0)
            return [];
        terms = KeepTermsPresentInAnyCandidate(terms, candidates);
        if (terms.Count == 0)
            return [];

        return candidates
            .Where(c => ContainsAllTerms(terms, c.Name, c.Description, c.Sku, c.CategoryName))
            .ToList();
    }

    private static IReadOnlyList<string> GetSelectionTerms(string selector, decimal? quantityToIgnore)
    {
        var ignoredQuantity = quantityToIgnore.HasValue
            ? decimal.Truncate(quantityToIgnore.Value).ToString(CultureInfo.InvariantCulture)
            : null;

        return CatalogSearchText.GetSearchTerms(selector)
            .Where(term => ignoredQuantity is null || !term.Equals(ignoredQuantity, StringComparison.Ordinal))
            .ToList();
    }

    private static IReadOnlyList<string> KeepTermsPresentInAnyCandidate(
        IReadOnlyList<string> terms,
        IReadOnlyList<ProductCandidate> candidates) =>
        terms
            .Where(term => candidates.Any(candidate => ContainsTerm(term,
                candidate.Name,
                candidate.Description,
                candidate.Sku,
                candidate.CategoryName)))
            .ToList();

    private static bool ContainsTerm(string term, params string?[] values)
    {
        var searchableTerms = CatalogSearchText.GetSearchTerms(
            string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v))));

        return searchableTerms.Any(searchable => searchable.Contains(term, StringComparison.Ordinal));
    }

    private static bool ContainsAllTerms(IReadOnlyList<string> terms, params string?[] values)
    {
        var searchableTerms = CatalogSearchText.GetSearchTerms(
            string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v))));

        return terms.All(term => searchableTerms.Any(searchable => searchable.Contains(term, StringComparison.Ordinal)));
    }

    private static bool IsExactIdentifierMatch(ProductCandidate candidate, string selector)
    {
        if (candidate.ProductId.HasValue
            && selector.Equals(candidate.ProductId.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            return true;

        return Matches(candidate.ExternalProductId, selector)
               || Matches(candidate.Sku, selector)
               || CatalogSearchText.NormalizeCompact(candidate.Name) == CatalogSearchText.NormalizeCompact(selector);
    }

    private static bool Matches(string? value, string selector) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Equals(selector, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<decimal> ExtractPriceCandidates(string selector)
    {
        var prices = new List<decimal>();
        foreach (Match match in Regex.Matches(selector, @"(?<value>\d{1,3}(?:[.,]\d{3})*|\d+)\s*(?<unit>mil|k)?", RegexOptions.IgnoreCase))
        {
            var raw = match.Groups["value"].Value.Replace(".", string.Empty).Replace(",", string.Empty);
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                continue;

            var hasThousandsUnit = match.Groups["unit"].Success;
            if (hasThousandsUnit)
            {
                prices.Add(value * 1000m);
                continue;
            }

            if (value >= 1000m)
                prices.Add(value);
            else if (value >= 10m)
                prices.Add(value * 1000m);
        }

        return prices.Distinct().ToList();
    }

    private static bool TryReadCandidate(AgentToolContext ctx, string key, out ProductCandidate candidate)
    {
        candidate = default!;
        if (!ctx.Facts.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<ProductCandidate>(raw, JsonOptions);
            if (parsed is null)
                return false;

            candidate = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatMoney(decimal value, string currency) =>
        string.Format(CultureInfo.InvariantCulture, "{0} {1:0}", currency, value);
}

internal sealed record ProductSearchSnapshot(
    string? Query,
    string? UserMessage,
    List<ProductCandidate> Products);

internal sealed record ProductCandidate(
    int Index,
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string Name,
    string? Description,
    string? CategoryName,
    decimal UnitPrice,
    string Currency,
    bool IsAvailable)
{
    public static ProductCandidate From(ProductReference product, int index) =>
        new(
            index,
            product.ProductId,
            product.ExternalProductId,
            Truncate(product.Sku, 80),
            Truncate(product.Name, 140) ?? string.Empty,
            Truncate(product.Description, 180),
            Truncate(product.CategoryName, 80),
            product.EffectiveUnitPrice ?? product.UnitPrice,
            product.Currency,
            product.IsAvailable);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
