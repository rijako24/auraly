using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Operations.Support;

internal static class CatalogQueryMemory
{
    public const string FactKey = "system.catalog_query_cursor";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task RememberCategoriesAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        int page,
        int pageSize,
        bool hasMore,
        CancellationToken ct) =>
        SaveAsync(facts, context, new CatalogQueryCursorState(
            SchemaVersion,
            CatalogQueryCursorKind.Categories,
            [],
            null,
            null,
            null,
            null,
            checked(page + 1),
            pageSize,
            false,
            hasMore,
            null), ct);

    public static Task RememberProductsAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        ProductSearchRequest request,
        IReadOnlyList<string> queries,
        bool hasMore,
        string? replacementReference,
        CancellationToken ct) =>
        SaveAsync(facts, context, new CatalogQueryCursorState(
            SchemaVersion,
            CatalogQueryCursorKind.Products,
            queries,
            request.Category,
            request.Family,
            request.Subcategory,
            request.ProductClass,
            checked(request.Page + 1),
            request.Limit,
            request.IncludeStock,
            hasMore,
            replacementReference), ct);

    public static CatalogQueryCursorState? Read(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var state = JsonSerializer.Deserialize<CatalogQueryCursorState>(raw, JsonOptions);
            return state is { SchemaVersion: SchemaVersion } ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task SaveAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        CatalogQueryCursorState state,
        CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(state, JsonOptions);
        await facts.SetAsync(
            context.ConversationId,
            context.BusinessId,
            FactKey,
            value,
            rememberAcrossRequests: false,
            ct);
        context.Facts[FactKey] = value;
    }
}

internal enum CatalogQueryCursorKind
{
    Categories = 0,
    Products = 1
}

internal sealed record CatalogQueryCursorState(
    int SchemaVersion,
    CatalogQueryCursorKind Kind,
    IReadOnlyList<string> Queries,
    string? Category,
    string? Family,
    string? Subcategory,
    string? ProductClass,
    int NextPage,
    int PageSize,
    bool IncludeStock,
    bool HasMore,
    string? ReplacementReference);
