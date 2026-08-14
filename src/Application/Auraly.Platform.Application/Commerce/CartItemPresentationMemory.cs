using System.Text.Json;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Catalog;

namespace Auraly.Platform.Application.Commerce;

internal static class CartItemPresentationMemory
{
    internal const string FactKey = "system.cart_item_presentation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<OrderSnapshot?> UpdateAndDecorateAsync(
        IConversationFactsService? facts,
        AgentConversationContext context,
        OrderSnapshot? snapshot,
        IReadOnlyList<CartItemPresentationRequest> requests,
        CancellationToken cancellationToken)
    {
        if (snapshot is null)
            return null;

        var entries = Read(context.Facts).ToList();
        foreach (var request in requests.Where(request =>
                     request.Command.Operation.Equals(CartCommandOperations.Add, StringComparison.OrdinalIgnoreCase)))
        {
            var item = FindItem(snapshot.Items, request.Command);
            if (item is null || FindEntry(entries, item) is not null)
                continue;
            if (SameLabel(request.RequestedName, item.ProductName))
                continue;
            entries.Add(new CartItemPresentationEntry(
                item.ProductId,
                item.ExternalProductId,
                item.Sku,
                item.ProductName,
                request.RequestedName.Trim()));
        }

        entries = entries
            .Where(entry => snapshot.Items.Any(item => Matches(entry, item)))
            .GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        await SaveAsync(facts, context, entries, cancellationToken);
        return Decorate(snapshot, entries);
    }

    public static OrderSnapshot Decorate(
        OrderSnapshot snapshot,
        IReadOnlyDictionary<string, string> facts) =>
        Decorate(snapshot, Read(facts));

    public static string? FindRequestedName(
        IReadOnlyDictionary<string, string> facts,
        Guid? productId,
        string? externalProductId,
        string? sku,
        string resolvedName)
    {
        var probe = new OrderItemSnapshot(
            Guid.Empty, productId, externalProductId, sku, resolvedName, 0m, 0m, 0m);
        return FindEntry(Read(facts), probe)?.RequestedName;
    }

    public static CartItemPresentationEntry? FindUniqueByReference(
        IReadOnlyDictionary<string, string> facts,
        string? reference,
        ProductMatchingPolicy? matchingPolicy = null)
    {
        var referenceTokens = ProductSearchText.GetMatchingTokens(reference);
        if (referenceTokens.Count == 0)
            return null;
        var threshold = matchingPolicy?.PendingReferenceSimilarity ?? 0.78d;
        var matches = Read(facts)
            .Select(entry => new
            {
                Entry = entry,
                Score = new[] { entry.RequestedName, entry.ResolvedName }
                    .Max(label => ProductSearchText.GetMatchingTokens(label).Sum(token =>
                        referenceTokens.Contains(token, StringComparer.Ordinal)
                            ? 10
                            : referenceTokens.Any(referenceToken =>
                                ProductSearchText.TokenSimilarity(token, referenceToken) >= threshold)
                                ? 1
                                : 0))
            })
            .Where(match => match.Score > 0)
            .ToList();
        if (matches.Count == 0)
            return null;
        var maximum = matches.Max(match => match.Score);
        var best = matches.Where(match => match.Score == maximum).ToList();
        return best.Count == 1 ? best[0].Entry : null;
    }
    private static OrderSnapshot Decorate(
        OrderSnapshot snapshot,
        IReadOnlyList<CartItemPresentationEntry> entries) =>
        snapshot with
        {
            Items = snapshot.Items.Select(item =>
                item with { RequestedName = FindEntry(entries, item)?.RequestedName }).ToList()
        };

    private static async Task SaveAsync(
        IConversationFactsService? facts,
        AgentConversationContext context,
        IReadOnlyList<CartItemPresentationEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            var hadStoredPresentation = context.Facts.Remove(FactKey);
            if (facts is not null && hadStoredPresentation)
                await facts.ClearFieldsAsync(context.ConversationId, [FactKey], cancellationToken);
            return;
        }

        var json = JsonSerializer.Serialize(new CartItemPresentationState(1, entries), JsonOptions);
        context.Facts[FactKey] = json;
        if (facts is not null)
        {
            await facts.SetAsync(
                context.ConversationId,
                context.BusinessId,
                FactKey,
                json,
                rememberAcrossRequests: false,
                cancellationToken);
        }
    }

    private static IReadOnlyList<CartItemPresentationEntry> Read(
        IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];
        try
        {
            return JsonSerializer.Deserialize<CartItemPresentationState>(raw, JsonOptions)?.Entries ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static OrderItemSnapshot? FindItem(
        IReadOnlyList<OrderItemSnapshot> items,
        ResolvedCartCommand command)
    {
        if (command.OrderItemId.HasValue)
        {
            var byItem = items.FirstOrDefault(item => item.OrderItemId == command.OrderItemId.Value);
            if (byItem is not null)
                return byItem;
        }

        return items.FirstOrDefault(item =>
            command.Product is not null
                && (command.Product.ProductId.HasValue && item.ProductId == command.Product.ProductId
                    || !string.IsNullOrWhiteSpace(command.Product.ExternalProductId)
                        && item.ExternalProductId?.Equals(
                            command.Product.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
                    || !string.IsNullOrWhiteSpace(command.Product.Sku)
                        && item.Sku?.Equals(command.Product.Sku, StringComparison.OrdinalIgnoreCase) == true
                    || SameLabel(item.ProductName, command.Product.Name)));
    }

    private static CartItemPresentationEntry? FindEntry(
        IEnumerable<CartItemPresentationEntry> entries,
        OrderItemSnapshot item) =>
        entries.LastOrDefault(entry => Matches(entry, item));

    private static bool Matches(CartItemPresentationEntry entry, OrderItemSnapshot item) =>
        entry.ProductId.HasValue && item.ProductId == entry.ProductId
        || !string.IsNullOrWhiteSpace(entry.ExternalProductId)
            && item.ExternalProductId?.Equals(entry.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
        || !string.IsNullOrWhiteSpace(entry.Sku)
            && item.Sku?.Equals(entry.Sku, StringComparison.OrdinalIgnoreCase) == true
        || SameLabel(entry.ResolvedName, item.ProductName);

    private static string Identity(CartItemPresentationEntry entry) =>
        entry.ProductId?.ToString("N")
        ?? entry.ExternalProductId?.Trim()
        ?? entry.Sku?.Trim()
        ?? CatalogSearchText.NormalizeCompact(entry.ResolvedName);

    private static bool SameLabel(string left, string right) =>
        CatalogSearchText.NormalizeCompact(left) == CatalogSearchText.NormalizeCompact(right);
}

internal sealed record CartItemPresentationRequest(
    ResolvedCartCommand Command,
    string RequestedName);

internal sealed record CartItemPresentationEntry(
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string ResolvedName,
    string RequestedName);

internal sealed record CartItemPresentationState(
    int SchemaVersion,
    IReadOnlyList<CartItemPresentationEntry> Entries);
