using System.Globalization;
using System.Text;
using MimosBabySpa.Application.Agents;
namespace MimosBabySpa.Application.Commerce;

public static class CartCommandOperations
{
    public const string Add = "add";
    public const string Remove = "remove";
    public const string CancelPending = "cancel_pending";
    public const string SetQuantity = "set_quantity";
}

public sealed record CartCommand(
    string Operation,
    string ProductText,
    decimal? Quantity,
    string? DestinationReference);

public sealed record ResolvedCartCommand(
    string Operation,
    ProductReference? Product,
    Guid? OrderItemId,
    decimal? Quantity,
    string ProductText);

public sealed record CartCommandCandidate(string Name, decimal UnitPrice, string Currency);

public sealed record CartCommandIssue(
    string Code,
    string ProductText,
    IReadOnlyList<string> Candidates)
{
    public IReadOnlyList<CartCommandCandidate> ProductCandidates { get; init; } = [];
    public decimal? RequestedQuantity { get; init; }
    public decimal? AvailableQuantity { get; init; }
    public decimal? ExistingCartQuantity { get; init; }
    public decimal? MaximumCommandQuantity { get; init; }
}

public sealed record CartCommandBatchResult(
    bool Success,
    string Code,
    OrderSnapshot? Snapshot,
    IReadOnlyList<CartCommandIssue> Issues);

public interface ICartProductResolver
{
    Task<IReadOnlyList<ProductReference>> FindAsync(
        AgentConversationContext context,
        string productText,
        CancellationToken cancellationToken = default);
}

public interface ICartMutationStore
{
    Task<OrderSnapshot> GetCurrentAsync(AgentConversationContext context, CancellationToken cancellationToken = default);

    Task<OrderSnapshot> ApplyAtomicallyAsync(
        AgentConversationContext context,
        IReadOnlyList<ResolvedCartCommand> commands,
        CancellationToken cancellationToken = default);
}

public sealed class CartCommandBatchProcessor
{
    private readonly ICartProductResolver _products;
    private readonly ICartMutationStore _store;

    public CartCommandBatchProcessor(ICartProductResolver products, ICartMutationStore store)
    {
        _products = products;
        _store = store;
    }

    public async Task<CartCommandBatchResult> ApplyAsync(
        AgentConversationContext context,
        IReadOnlyList<CartCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0)
            return new CartCommandBatchResult(true, "cart.no_changes", await _store.GetCurrentAsync(context, cancellationToken), []);

        var destinations = commands
            .Select(command => command.DestinationReference?.Trim())
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (destinations.Count > 1
            && (!context.Facts.TryGetValue("delivery_address", out var selectedDestination)
                || string.IsNullOrWhiteSpace(selectedDestination)))
        {
            return new CartCommandBatchResult(
                false,
                "cart.multiple_destinations",
                null,
                [new CartCommandIssue("multiple_destinations", string.Join(" | ", destinations), destinations!)]);
        }

        foreach (var command in commands)
        {
            if (command.Operation is not (CartCommandOperations.Add or CartCommandOperations.Remove or CartCommandOperations.SetQuantity))
                return Failure("cart.invalid_input", command.ProductText);
            if (string.IsNullOrWhiteSpace(command.ProductText))
                return Failure("cart.invalid_input", command.ProductText);
            if ((command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity)
                && command.Quantity is null or <= 0)
                return Failure("cart.invalid_input", command.ProductText);
        }

        var duplicate = commands
            .GroupBy(command => Normalize(command.ProductText), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Failure("cart.conflicting_commands", duplicate.First().ProductText);
        }

        var current = await _store.GetCurrentAsync(context, cancellationToken);
        var resolved = new List<ResolvedCartCommand>(commands.Count);
        foreach (var command in commands)
        {
            if (command.Operation.Equals(CartCommandOperations.Add, StringComparison.OrdinalIgnoreCase))
            {
                var candidates = await _products.FindAsync(context, command.ProductText, cancellationToken);
                var selected = SelectSingleProduct(command.ProductText, candidates);
                if (selected is null)
                {
                    return new CartCommandBatchResult(
                        false,
                        candidates.Count == 0 ? "cart.product_not_found" : "cart.product_ambiguous",
                        null,
                        [new CartCommandIssue(
                            candidates.Count == 0 ? "product_not_found" : "product_ambiguous",
                            command.ProductText,
                            candidates.Select(product => product.Name).ToList())
                        {
                            ProductCandidates = candidates.Select(product => new CartCommandCandidate(
                                product.Name, product.EffectiveUnitPrice ?? product.UnitPrice, product.Currency)).ToList()
                        }]);
                }

                resolved.Add(new ResolvedCartCommand(command.Operation, selected, null, command.Quantity, command.ProductText));
                continue;
            }

            var item = SelectSingleItem(command.ProductText, current.Items);
            if (item is null && command.Operation.Equals(CartCommandOperations.SetQuantity, StringComparison.OrdinalIgnoreCase))
            {
                var candidates = await _products.FindAsync(context, command.ProductText, cancellationToken);
                var selected = SelectSingleProduct(command.ProductText, candidates);
                if (selected is not null)
                {
                    resolved.Add(new ResolvedCartCommand(CartCommandOperations.Add, selected, null, command.Quantity, command.ProductText));
                    continue;
                }

                return new CartCommandBatchResult(
                    false,
                    candidates.Count == 0 ? "cart.product_not_found" : "cart.product_ambiguous",
                    null,
                    [new CartCommandIssue(
                        candidates.Count == 0 ? "product_not_found" : "product_ambiguous",
                        command.ProductText,
                        candidates.Select(product => product.Name).ToList())
                        {
                            ProductCandidates = candidates.Select(product => new CartCommandCandidate(
                                product.Name, product.EffectiveUnitPrice ?? product.UnitPrice, product.Currency)).ToList()
                        }]);
            }

            if (item is null)
            {
                return new CartCommandBatchResult(
                    false,
                    "cart.item_not_found_or_ambiguous",
                    null,
                    [new CartCommandIssue("item_not_found_or_ambiguous", command.ProductText, current.Items.Select(value => value.ProductName).ToList())]);
            }

            ProductReference? quantityProduct = null;
            if (command.Operation == CartCommandOperations.SetQuantity && command.Quantity > item.Quantity)
            {
                var candidates = await _products.FindAsync(
                    context,
                    command.ProductText,
                    cancellationToken);
                quantityProduct = SelectSingleProduct(item.Sku ?? item.ProductName, candidates)
                    ?? SelectSingleProduct(item.ProductName, candidates);
                if (quantityProduct is null)
                {
                    return new CartCommandBatchResult(
                        false,
                        candidates.Count == 0 ? "cart.product_not_found" : "cart.product_ambiguous",
                        null,
                        [new CartCommandIssue(
                            candidates.Count == 0 ? "product_not_found" : "product_ambiguous",
                            command.ProductText,
                            candidates.Select(product => product.Name).ToList())
                        {
                            ProductCandidates = candidates.Select(product => new CartCommandCandidate(
                                product.Name, product.EffectiveUnitPrice ?? product.UnitPrice, product.Currency)).ToList()
                        }]);
                }
            }

            resolved.Add(new ResolvedCartCommand(command.Operation, quantityProduct, item.OrderItemId, command.Quantity, command.ProductText));
        }

        var stockIssue = FindStockIssue(current, resolved);
        if (stockIssue is not null)
            return new CartCommandBatchResult(false, "cart.insufficient_stock", null, [stockIssue]);

        var snapshot = await _store.ApplyAtomicallyAsync(context, resolved, cancellationToken);
        return new CartCommandBatchResult(true, "cart.applied", snapshot, []);
    }

    private static CartCommandBatchResult Failure(string code, string productText) =>
        new(false, code, null, [new CartCommandIssue(code, productText, [])]);

    private static ProductReference? SelectSingleProduct(string text, IReadOnlyList<ProductReference> candidates)
    {
        var normalized = Normalize(text);
        var exact = candidates.Where(candidate =>
                Normalize(candidate.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || Normalize(candidate.Sku).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1)
            return exact[0];
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static OrderItemSnapshot? SelectSingleItem(string text, IReadOnlyList<OrderItemSnapshot> items)
    {
        var normalized = Normalize(text);
        var exact = items.Where(item =>
                Normalize(item.ProductName).Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || Normalize(item.Sku).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1)
            return exact[0];

        var selectorTerms = SearchTerms(text);
        var partial = items.Where(item =>
                Normalize(item.ProductName).Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || selectorTerms.All(term => SearchTerms($"{item.ProductName} {item.Sku}").Contains(term)))
            .ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private static CartCommandIssue? FindStockIssue(
        OrderSnapshot current,
        IReadOnlyList<ResolvedCartCommand> commands)
    {
        var stockMutations = commands
            .Where(command => command.Product?.StockQuantity is not null)
            .GroupBy(command => ProductKey(command.Product!), StringComparer.OrdinalIgnoreCase);

        foreach (var group in stockMutations)
        {
            var product = group.First().Product!;
            var existingQuantity = current.Items
                .Where(item => IsSameProduct(item, product))
                .Sum(item => item.Quantity);
            var requestedQuantity = group.Any(command => command.Operation == CartCommandOperations.SetQuantity)
                ? group.Last(command => command.Operation == CartCommandOperations.SetQuantity).Quantity ?? existingQuantity
                : existingQuantity + group.Sum(command => command.Quantity ?? 0m);
            if (requestedQuantity <= product.StockQuantity!.Value)
                continue;

            return new CartCommandIssue("insufficient_stock", product.Name, [])
            {
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = product.StockQuantity.Value,
                ExistingCartQuantity = existingQuantity,
                MaximumCommandQuantity = group.Any(command => command.Operation == CartCommandOperations.SetQuantity)
                    ? product.StockQuantity.Value
                    : Math.Max(product.StockQuantity.Value - existingQuantity, 0m)
            };
        }

        return null;
    }

    private static bool IsSameProduct(OrderItemSnapshot item, ProductReference product) =>
        product.ProductId.HasValue && item.ProductId == product.ProductId
        || !string.IsNullOrWhiteSpace(product.ExternalProductId)
            && item.ExternalProductId?.Equals(product.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
        || !string.IsNullOrWhiteSpace(product.Sku)
            && item.Sku?.Equals(product.Sku, StringComparison.OrdinalIgnoreCase) == true
        || Normalize(item.ProductName).Equals(Normalize(product.Name), StringComparison.Ordinal);

    private static string ProductKey(ProductReference product) =>
        product.ProductId?.ToString("N")
        ?? product.ExternalProductId?.Trim().ToLowerInvariant()
        ?? product.Sku?.Trim().ToLowerInvariant()
        ?? Normalize(product.Name);

    private static IReadOnlySet<string> SearchTerms(string? value) =>
        (value ?? string.Empty)
            .Split([' ', '-', '_', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(term => term.Length > 1 && !decimal.TryParse(term, out _))
            .Select(Singularize)
            .ToHashSet(StringComparer.Ordinal);

    private static string Singularize(string value) =>
        value.Length > 3 && value.EndsWith('s') ? value[..^1] : value;
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
    }
}
