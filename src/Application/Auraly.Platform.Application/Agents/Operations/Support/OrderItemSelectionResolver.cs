using System.Text.Json;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Catalog;

namespace Auraly.Platform.Application.Agents.Operations.Support;

internal static class OrderItemSelectionResolver
{
    public static bool TryResolve(
        JsonElement arguments,
        OrderSnapshot draft,
        out OrderItemSnapshot item,
        out IReadOnlyList<OrderItemSnapshot> ambiguous)
    {
        item = default!;
        ambiguous = [];

        if (draft.Items.Count == 0)
            return false;

        var rawOrderItemId = OperationJsonHelper.TryGetString(arguments, "order_item_id", out var oid) ? oid : null;
        if (Guid.TryParse(rawOrderItemId, out var parsedOrderItemId))
        {
            var matches = draft.Items.Where(i => i.OrderItemId == parsedOrderItemId).ToList();
            return ResolveMatches(matches, out item, out ambiguous);
        }

        var productId = OperationJsonHelper.TryGetString(arguments, "product_id", out var pid) ? pid : null;
        if (Guid.TryParse(productId, out var parsedProductId))
        {
            var matches = draft.Items.Where(i => i.ProductId == parsedProductId).ToList();
            return ResolveMatches(matches, out item, out ambiguous);
        }

        var externalProductId = OperationJsonHelper.TryGetString(arguments, "external_product_id", out var ext) ? ext : null;
        if (!string.IsNullOrWhiteSpace(externalProductId))
        {
            var matches = draft.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.ExternalProductId)
                            && i.ExternalProductId.Equals(externalProductId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 0)
                return ResolveMatches(matches, out item, out ambiguous);
        }

        var sku = OperationJsonHelper.TryGetString(arguments, "sku", out var s) ? s : null;
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var matches = draft.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Sku)
                            && i.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 0)
                return ResolveMatches(matches, out item, out ambiguous);
        }

        var name = OperationJsonHelper.TryGetString(arguments, "name", out var n) ? n : null;
        var selector = FirstMeaningfulSelector(name, sku, externalProductId, productId, rawOrderItemId);
        if (string.IsNullOrWhiteSpace(selector))
        {
            if (draft.Items.Count == 1)
            {
                item = draft.Items[0];
                return true;
            }

            return false;
        }

        var nameMatches = FindNameMatches(draft.Items, selector, TryGetQuantityToIgnore(arguments))
            .DistinctBy(i => i.OrderItemId)
            .ToList();

        return ResolveMatches(nameMatches, out item, out ambiguous);
    }

    private static bool ResolveMatches(
        IReadOnlyList<OrderItemSnapshot> matches,
        out OrderItemSnapshot item,
        out IReadOnlyList<OrderItemSnapshot> ambiguous)
    {
        item = default!;
        ambiguous = [];

        if (matches.Count == 1)
        {
            item = matches[0];
            return true;
        }

        if (matches.Count > 1)
            ambiguous = matches;

        return false;
    }

    private static IEnumerable<OrderItemSnapshot> FindNameMatches(
        IReadOnlyList<OrderItemSnapshot> items,
        string selector,
        decimal? quantityToIgnore)
    {
        var normalizedSelector = CatalogSearchText.NormalizeCompact(selector);
        if (string.IsNullOrWhiteSpace(normalizedSelector))
            yield break;

        var exactNameMatches = items
            .Where(i => CatalogSearchText.NormalizeCompact(i.ProductName) == normalizedSelector)
            .ToList();
        if (exactNameMatches.Count > 0)
        {
            foreach (var match in exactNameMatches)
                yield return match;
            yield break;
        }

        var selectorTerms = GetSelectorTermsPresentInCart(selector, items, quantityToIgnore);
        if (selectorTerms.Count == 0)
            yield break;

        var phraseMatches = items
            .Where(i => ContainsOrderedTerms(selectorTerms, GetItemTerms(i)))
            .ToList();
        if (phraseMatches.Count > 0)
        {
            foreach (var match in phraseMatches)
                yield return match;
            yield break;
        }

        foreach (var item in items.Where(i => ContainsAllWholeTerms(selectorTerms, GetItemTerms(i))))
            yield return item;
    }

    private static IReadOnlyList<string> GetSelectorTermsPresentInCart(
        string selector,
        IReadOnlyList<OrderItemSnapshot> items,
        decimal? quantityToIgnore)
    {
        var ignoredQuantity = quantityToIgnore.HasValue
            ? decimal.Truncate(quantityToIgnore.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        var itemTerms = items
            .SelectMany(GetItemTerms)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return CatalogSearchText.GetSearchTerms(selector)
            .Where(term => ignoredQuantity is null || !term.Equals(ignoredQuantity, StringComparison.Ordinal))
            .Where(term => itemTerms.Any(itemTerm => TermsMatch(term, itemTerm)))
            .ToList();
    }

    private static IReadOnlyList<string> GetItemTerms(OrderItemSnapshot item) =>
        CatalogSearchText.GetSearchTerms(string.Join(' ', item.ProductName, item.Sku));

    private static bool ContainsOrderedTerms(IReadOnlyList<string> selectorTerms, IReadOnlyList<string> itemTerms)
    {
        if (selectorTerms.Count == 0 || selectorTerms.Count > itemTerms.Count)
            return false;

        for (var start = 0; start <= itemTerms.Count - selectorTerms.Count; start++)
        {
            var allMatch = true;
            for (var offset = 0; offset < selectorTerms.Count; offset++)
            {
                if (TermsMatch(selectorTerms[offset], itemTerms[start + offset]))
                    continue;

                allMatch = false;
                break;
            }

            if (allMatch)
                return true;
        }

        return false;
    }

    private static bool ContainsAllWholeTerms(IReadOnlyList<string> selectorTerms, IReadOnlyList<string> itemTerms) =>
        selectorTerms.All(selectorTerm => itemTerms.Any(itemTerm => TermsMatch(selectorTerm, itemTerm)));

    private static bool TermsMatch(string selectorTerm, string itemTerm) =>
        itemTerm.Equals(selectorTerm, StringComparison.Ordinal)
        || Singularize(itemTerm).Equals(Singularize(selectorTerm), StringComparison.Ordinal);

    private static string Singularize(string term) =>
        term.Length > 3 && term.EndsWith('s') ? term[..^1] : term;

    private static string? FirstMeaningfulSelector(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && !decimal.TryParse(v.Trim(), out _));

    private static decimal? TryGetQuantityToIgnore(JsonElement arguments) =>
        arguments.TryGetProperty("quantity", out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDecimal(out var quantity)
            ? quantity
            : null;
}
