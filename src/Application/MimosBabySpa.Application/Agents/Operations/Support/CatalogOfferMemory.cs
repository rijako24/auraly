using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Agents.Operations.Support;

internal static class CatalogOfferMemory
{
    public const string FactKey = "system.catalog_products";
    private const int SchemaVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task RememberAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        IReadOnlyList<ProductReference> products,
        IReadOnlyList<string> searchTerms,
        CancellationToken cancellationToken,
        string? explicitReplacementReference = null)
    {
        var active = products.Where(product => product.IsActive)
            .Select(ProductCandidate.From)
            .GroupBy(ProductIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (active.Count == 0)
            return;

        var normalizedSearchTerms = searchTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasSingleSearchTerm = normalizedSearchTerms.Count == 1;
        var replacementReference = hasSingleSearchTerm && !string.IsNullOrWhiteSpace(explicitReplacementReference)
            ? explicitReplacementReference.Trim()
            : hasSingleSearchTerm && CommerceConversationMatcher.Matches(
                context.LatestUserMessage, context.Config?.Commerce.Conversation.ProductReplacementRules)
                ? normalizedSearchTerms[0]
                : null;
        var existing = Read(context.Facts);
        var contextAnchorTerms = ResolveContextAnchorTerms(existing, normalizedSearchTerms);
        var sequence = (existing?.Sequence ?? 0) + 1;
        var snapshots = (existing?.Snapshots ?? [])
            .Append(new CatalogOfferSnapshot(
                sequence,
                DateTime.UtcNow,
                normalizedSearchTerms,
                active,
                replacementReference,
                contextAnchorTerms))
            .ToList();

        var maxSnapshots = Math.Clamp(context.Config?.Commerce.OfferMemoryMaxSnapshots ?? 8, 1, 50);
        var maxProducts = Math.Clamp(context.Config?.Commerce.OfferMemoryMaxProducts ?? 100, 1, 500);
        var memory = Trim(new CatalogOfferState(SchemaVersion, sequence, snapshots), maxSnapshots, maxProducts);
        var value = JsonSerializer.Serialize(memory, JsonOptions);
        await facts.SetAsync(
            context.ConversationId,
            context.BusinessId,
            FactKey,
            value,
            rememberAcrossRequests: false,
            cancellationToken);
        context.Facts[FactKey] = value;
    }

    public static CatalogOfferState? Read(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacy = JsonSerializer.Deserialize<List<ProductCandidate>>(raw, LegacyJsonOptions) ?? [];
                return legacy.Count == 0
                    ? null
                    : new CatalogOfferState(
                        SchemaVersion,
                        1,
                        [new CatalogOfferSnapshot(1, null, [], legacy)]);
            }

            var state = JsonSerializer.Deserialize<CatalogOfferState>(raw, JsonOptions);
            return state is { Snapshots.Count: > 0 } ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ProductCandidate> AllProducts(CatalogOfferState state)
    {
        var latest = new Dictionary<string, ProductCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in state.Snapshots.OrderBy(snapshot => snapshot.Sequence))
        foreach (var product in snapshot.Products)
            latest[ProductIdentity(product)] = product;
        return latest.Values.ToList();
    }
    public static bool IsLatestOfferForeground(
        CatalogOfferState state,
        string? lastBotMessage,
        double minimumCoverage)
    {
        if (string.IsNullOrWhiteSpace(lastBotMessage))
            return false;

        var latest = state.Snapshots.MaxBy(snapshot => snapshot.Sequence);
        if (latest is null || latest.Products.Count == 0)
            return false;

        var botTokens = ProductSearchText.GetMatchingTokens(lastBotMessage)
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
        var presentedProducts = latest.Products.Count(product =>
        {
            var productTokens = ProductSearchText.GetMatchingTokens(product.Name)
                .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
                .ToList();
            if (productTokens.Count == 0)
                return false;
            var covered = productTokens.Count(botTokens.Contains);
            return covered / (double)productTokens.Count
                >= Math.Clamp(minimumCoverage, 0d, 1d);
        });
        return presentedProducts >= Math.Min(2, latest.Products.Count);
    }



    private static CatalogOfferState Trim(CatalogOfferState state, int maxSnapshots, int maxProducts)
    {
        var recent = state.Snapshots
            .OrderByDescending(snapshot => snapshot.Sequence)
            .Take(maxSnapshots)
            .ToList();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<CatalogOfferSnapshot>();
        foreach (var snapshot in recent)
        {
            var products = new List<ProductCandidate>();
            foreach (var product in snapshot.Products)
            {
                var identity = ProductIdentity(product);
                if (identities.Contains(identity))
                    continue;
                if (identities.Count >= maxProducts)
                    continue;
                identities.Add(identity);
                products.Add(product);
            }
            if (products.Count > 0)
                kept.Add(snapshot with { Products = products });
        }

        kept.Reverse();
        return state with { Snapshots = kept };
    }

    private static IReadOnlyList<string> ResolveContextAnchorTerms(
        CatalogOfferState? existing,
        IReadOnlyList<string> currentTerms)
    {
        if (currentTerms.Count == 0)
            return [];

        var latest = existing?.Snapshots.MaxBy(snapshot => snapshot.Sequence);
        var previousAnchor = latest?.ContextAnchorTerms is { Count: > 0 }
            ? latest.ContextAnchorTerms
            : latest?.SearchTerms;
        if (previousAnchor is not { Count: 1 })
            return currentTerms;

        var anchorTokens = ProductSearchText.GetMatchingTokens(previousAnchor[0]);
        return anchorTokens.Count > 0
            && currentTerms.All(term =>
                anchorTokens.All(ProductSearchText.GetMatchingTokens(term).Contains))
                ? previousAnchor
                : currentTerms;
    }

    private static string ProductIdentity(ProductCandidate product)
    {
        if (product.ProductId.HasValue)
            return $"id:{product.ProductId.Value:N}";
        if (!string.IsNullOrWhiteSpace(product.ExternalProductId))
            return $"external:{product.ExternalProductId.Trim()}";
        if (!string.IsNullOrWhiteSpace(product.Sku))
            return $"sku:{product.Sku.Trim()}";
        return $"name:{product.Name.Trim()}";
    }
}

internal sealed record CatalogOfferState(
    int SchemaVersion,
    long Sequence,
    IReadOnlyList<CatalogOfferSnapshot> Snapshots);

internal sealed record CatalogOfferSnapshot(
    long Sequence,
    DateTime? OfferedAtUtc,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<ProductCandidate> Products,
    string? ReplacementReference = null,
    IReadOnlyList<string>? ContextAnchorTerms = null);
