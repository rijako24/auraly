using System.Text.Json;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Support;

internal static class CatalogRecommendationMemory
{
    public const string FactKey = "system.catalog_recommendations";
    private const int SchemaVersion = 1;
    private const int MaximumProducts = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RememberAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        ProductReference product,
        CancellationToken cancellationToken)
    {
        var existing = Read(context.Facts)?.Products ?? [];
        var products = existing
            .Append(ProductCandidate.From(product))
            .GroupBy(ProductIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .TakeLast(MaximumProducts)
            .ToList();
        var value = JsonSerializer.Serialize(
            new CatalogRecommendationState(SchemaVersion, products),
            JsonOptions);
        await facts.SetAsync(
            context.ConversationId,
            context.BusinessId,
            FactKey,
            value,
            rememberAcrossRequests: false,
            cancellationToken);
        context.Facts[FactKey] = value;
    }

    public static CatalogRecommendationState? Read(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<CatalogRecommendationState>(raw, JsonOptions);
            return state is { Products.Count: > 0 } ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

internal sealed record CatalogRecommendationState(
    int SchemaVersion,
    IReadOnlyList<ProductCandidate> Products);
