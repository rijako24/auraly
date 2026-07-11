using System.Globalization;
using System.Text;
using MimosBabySpa.Application.Agents;
namespace MimosBabySpa.Application.Commerce;

public static class CartCommandOperations
{
    public const string Add = "add";
    public const string Remove = "remove";
    public const string SetQuantity = "set_quantity";
}

public sealed record CartCommand(
    string Operation,
    string ProductText,
    decimal? Quantity,
    string? GroupReference);

public sealed record ResolvedCartCommand(
    string Operation,
    ProductReference? Product,
    Guid? OrderItemId,
    decimal? Quantity,
    string ProductText);

public sealed record CartCommandIssue(
    string Code,
    string ProductText,
    IReadOnlyList<string> Candidates);

public sealed record CartCommandBatchResult(
    bool Success,
    string Code,
    OrderSnapshot? Snapshot,
    IReadOnlyList<CartCommandIssue> Issues);

public interface ICartProductResolver
{
    Task<IReadOnlyList<ProductReference>> FindAsync(
        AgentToolContext context,
        string productText,
        CancellationToken cancellationToken = default);
}

public interface ICartMutationStore
{
    Task<OrderSnapshot> GetCurrentAsync(AgentToolContext context, CancellationToken cancellationToken = default);

    Task<OrderSnapshot> ApplyAtomicallyAsync(
        AgentToolContext context,
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
        AgentToolContext context,
        IReadOnlyList<CartCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0)
            return new CartCommandBatchResult(true, "cart.no_changes", await _store.GetCurrentAsync(context, cancellationToken), []);

        var groups = commands
            .Select(command => command.GroupReference?.Trim())
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count > 1)
        {
            return new CartCommandBatchResult(
                false,
                "cart.multiple_orders",
                null,
                [new CartCommandIssue("multiple_orders", string.Join(" | ", groups), groups!)]);
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
                            candidates.Select(product => product.Name).ToList())]);
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
                        candidates.Select(product => product.Name).ToList())]);
            }

            if (item is null)
            {
                return new CartCommandBatchResult(
                    false,
                    "cart.item_not_found_or_ambiguous",
                    null,
                    [new CartCommandIssue("item_not_found_or_ambiguous", command.ProductText, current.Items.Select(value => value.ProductName).ToList())]);
            }

            resolved.Add(new ResolvedCartCommand(command.Operation, null, item.OrderItemId, command.Quantity, command.ProductText));
        }

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

        var partial = items.Where(item => Normalize(item.ProductName).Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

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
