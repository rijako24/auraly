using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class ProductSelectionMemory
{
    private const string SelectedProductKey = "system.selected_product";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static async Task<ProductCandidate?> RememberSearchAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        ProductSearchResult result,
        CancellationToken ct)
    {
        var selected = InferSingleCandidate(result.Products);
        if (selected is not null)
            await RememberSelectedAsync(factsService, ctx, selected, ct);
        else
            await ClearSelectedAsync(factsService, ctx, ct);

        return selected;
    }

    public static bool TryGetSelected(AgentToolContext ctx, out ProductCandidate candidate) =>
        TryReadCandidate(ctx, SelectedProductKey, out candidate);

    public static async Task ClearSelectedAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        await factsService.ClearFieldsAsync(ctx.ConversationId, [SelectedProductKey], ct);
        ctx.Facts.Remove(SelectedProductKey);
    }

    private static async Task RememberSelectedAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        ProductCandidate candidate,
        CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(candidate, JsonOptions);
        await factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, SelectedProductKey, value, rememberAcrossRequests: false, ct);
        ctx.Facts[SelectedProductKey] = value;
    }

    private static ProductCandidate? InferSingleCandidate(IReadOnlyList<ProductReference> products) =>
        products.Count == 1 ? ProductCandidate.From(products[0]) : null;

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
}

internal sealed record ProductCandidate(
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string Name,
    decimal UnitPrice)
{
    public static ProductCandidate From(ProductReference product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            Truncate(product.Sku, 80),
            Truncate(product.Name, 140) ?? string.Empty,
            product.EffectiveUnitPrice ?? product.UnitPrice);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
