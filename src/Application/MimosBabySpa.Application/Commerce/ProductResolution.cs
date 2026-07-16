using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Commerce;

public enum ProductResolutionStatus
{
    Resolved,
    SuggestionRequired,
    Ambiguous,
    NotFound,
    Unavailable
}

public enum ProductMatchSource
{
    Catalog,
    RememberedCatalog,
    BusinessAlias,
    CustomerAlias,
    LocalLexicalIndex
}

public sealed record RetrievedProductCandidate(
    ProductReference Product,
    ProductMatchSource Source,
    bool ExactAlias = false,
    bool CanAutoResolve = false);

public sealed record ProductResolutionCandidate(
    ProductReference Product,
    double Score,
    ProductMatchSource Source);

public sealed record ProductResolution(
    ProductResolutionStatus Status,
    ProductReference? Selected,
    IReadOnlyList<ProductResolutionCandidate> Candidates,
    string RequestedText)
{
    public static ProductResolution NotFound(string text) =>
        new(ProductResolutionStatus.NotFound, null, [], text);
}

public static class ProductResolutionEngine
{
    private const double CandidateThreshold = 0.56d;
    private const double SuggestionThreshold = 0.68d;
    private const double SuggestionMargin = 0.12d;
    private const double MeaningfulTermSimilarity = 0.62d;
    private const double MinimumSemanticCoverage = 0.70d;

    public static ProductResolution Resolve(
        string requestedText,
        IReadOnlyList<RetrievedProductCandidate> retrieved)
    {
        if (string.IsNullOrWhiteSpace(requestedText) || retrieved.Count == 0)
            return ProductResolution.NotFound(requestedText);

        var allCandidates = retrieved
            .GroupBy(value => ProductIdentity(value.Product), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(value => value.ExactAlias)
                .ThenByDescending(value => value.CanAutoResolve)
                .First())
            .ToList();
        var candidates = allCandidates.Where(value => value.Product.IsActive).ToList();

        var autoAliases = candidates
            .Where(candidate => candidate.ExactAlias && candidate.CanAutoResolve)
            .ToList();
        if (autoAliases.Count == 1)
        {
            var selected = autoAliases[0];
            return new ProductResolution(
                ProductResolutionStatus.Resolved,
                selected.Product,
                [new ProductResolutionCandidate(selected.Product, 1d, selected.Source)],
                requestedText);
        }

        var exactAliases = candidates.Where(candidate => candidate.ExactAlias).ToList();
        if (exactAliases.Count > 0)
        {
            var aliasCandidates = exactAliases.Select(candidate => new ProductResolutionCandidate(
                candidate.Product, 1d, candidate.Source)).ToList();
            return new ProductResolution(
                exactAliases.Count == 1
                    ? ProductResolutionStatus.SuggestionRequired
                    : ProductResolutionStatus.Ambiguous,
                null,
                aliasCandidates,
                requestedText);
        }

        var unavailableAliases = allCandidates
            .Where(candidate => !candidate.Product.IsActive && candidate.ExactAlias)
            .Select(candidate => new ProductResolutionCandidate(
                candidate.Product, 1d, candidate.Source))
            .ToList();
        if (unavailableAliases.Count > 0)
        {
            return new ProductResolution(
                ProductResolutionStatus.Unavailable, null,
                unavailableAliases.Take(3).ToList(), requestedText);
        }

        var exact = candidates
            .Where(candidate => IsExactIdentity(requestedText, candidate.Product))
            .ToList();
        if (exact.Count == 1)
        {
            var selected = exact[0];
            return new ProductResolution(
                ProductResolutionStatus.Resolved,
                selected.Product,
                [new ProductResolutionCandidate(selected.Product, 1d, selected.Source)],
                requestedText);
        }

        var unavailableIdentity = allCandidates
            .Where(candidate => !candidate.Product.IsActive
                && IsExactIdentity(requestedText, candidate.Product))
            .Select(candidate => new ProductResolutionCandidate(
                candidate.Product, 1d, candidate.Source))
            .ToList();
        if (unavailableIdentity.Count > 0)
        {
            return new ProductResolution(
                ProductResolutionStatus.Unavailable, null,
                unavailableIdentity.Take(3).ToList(), requestedText);
        }
        var scored = candidates
            .Select(candidate => new ProductResolutionCandidate(
                candidate.Product,
                Score(requestedText, candidate.Product),
                candidate.Source))
            .Where(candidate => HasMinimumSemanticCoverage(requestedText, candidate.Product))
            .Where(candidate => candidate.Score >= CandidateThreshold)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Product.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (scored.Count == 0)
        {
            var unavailable = allCandidates
                .Where(candidate => !candidate.Product.IsActive)
                .Select(candidate => new ProductResolutionCandidate(
                    candidate.Product, Score(requestedText, candidate.Product), candidate.Source))
                .Where(candidate => candidate.Score >= SuggestionThreshold)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Product.Name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (unavailable.Count > 0)
                return new ProductResolution(
                    ProductResolutionStatus.Unavailable, null, unavailable, requestedText);
            return ProductResolution.NotFound(requestedText);
        }

        var exactTermMatches = scored
            .Where(candidate => ContainsEveryRequestedTerm(requestedText, candidate.Product))
            .ToList();
        if (exactTermMatches.Count == 1)
        {
            return new ProductResolution(
                ProductResolutionStatus.Resolved,
                exactTermMatches[0].Product,
                exactTermMatches,
                requestedText);
        }

        var top = scored[0];
        var margin = scored.Count == 1 ? 1d : top.Score - scored[1].Score;
        if (top.Score >= SuggestionThreshold && margin >= SuggestionMargin)
        {
            return new ProductResolution(
                ProductResolutionStatus.SuggestionRequired,
                null,
                [top],
                requestedText);
        }

        return new ProductResolution(
            ProductResolutionStatus.Ambiguous,
            null,
            scored.Take(3).ToList(),
            requestedText);
    }

    public static double Score(string requestedText, ProductReference product)
    {
        var requested = ProductSearchText.GetTokens(requestedText);
        var offered = ProductSearchText.GetTokens($"{product.Name} {product.Sku} {product.ExternalProductId} {product.CategoryName}");
        if (requested.Count == 0 || offered.Count == 0)
            return 0d;

        var requestedNumbers = requested.Where(token => token.All(char.IsDigit)).ToHashSet(StringComparer.Ordinal);
        var offeredNumbers = offered.Where(token => token.All(char.IsDigit)).ToHashSet(StringComparer.Ordinal);
        if (requestedNumbers.Count > 0 && !requestedNumbers.IsSubsetOf(offeredNumbers))
            return 0.25d;

        var tokenScores = requested
            .Where(token => !token.All(char.IsDigit))
            .Select(token => offered.Where(candidate => !candidate.All(char.IsDigit))
                .Select(candidate => ProductSearchText.TokenSimilarity(token, candidate))
                .DefaultIfEmpty(0d)
                .Max())
            .ToList();
        if (tokenScores.Count == 0)
            return requestedNumbers.IsSubsetOf(offeredNumbers) ? 0.8d : 0d;

        var average = tokenScores.Average();
        var weakest = tokenScores.Min();
        var coverage = tokenScores.Count(score => score >= 0.62d) / (double)tokenScores.Count;
        return Math.Clamp(average * 0.55d + weakest * 0.2d + coverage * 0.25d, 0d, 1d);
    }

    private static bool IsExactIdentity(string requestedText, ProductReference product)
    {
        var normalized = CatalogSearchText.NormalizeCompact(requestedText);
        return normalized.Length > 0 &&
            (normalized == CatalogSearchText.NormalizeCompact(product.Name)
             || normalized == CatalogSearchText.NormalizeCompact(product.Sku)
             || normalized == CatalogSearchText.NormalizeCompact(product.ExternalProductId));
    }

    private static bool ContainsEveryRequestedTerm(string requestedText, ProductReference product)
    {
        var requested = ProductSearchText.GetTokens(requestedText).ToHashSet(StringComparer.Ordinal);
        var offered = ProductSearchText.GetTokens($"{product.Name} {product.Sku} {product.ExternalProductId}").ToHashSet(StringComparer.Ordinal);
        return requested.Count > 0 && requested.IsSubsetOf(offered);
    }

    private static bool HasMinimumSemanticCoverage(string requestedText, ProductReference product)
    {
        var requested = ProductSearchText.GetTokens(requestedText)
            .Where(token => !token.All(char.IsDigit))
            .ToList();
        var offered = ProductSearchText.GetTokens(
                $"{product.Name} {product.Sku} {product.ExternalProductId} {product.CategoryName}")
            .Where(token => !token.All(char.IsDigit))
            .ToList();
        if (requested.Count == 0 || offered.Count == 0)
            return false;

        var covered = requested.Count(token => offered.Any(candidate =>
            ProductSearchText.TokenSimilarity(token, candidate) >= MeaningfulTermSimilarity
            || token.Length >= 3 && token.Length < candidate.Length
                && candidate.StartsWith(token, StringComparison.Ordinal)));
        var required = requested.Count == 1
            ? 1
            : (int)Math.Ceiling(requested.Count * MinimumSemanticCoverage);
        return covered >= required;
    }

    private static string ProductIdentity(ProductReference product) =>
        product.ProductId?.ToString("N")
        ?? product.ExternalProductId?.Trim()
        ?? product.Sku?.Trim()
        ?? CatalogSearchText.NormalizeCompact(product.Name);
}
