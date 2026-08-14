using System.Globalization;
using System.Text;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Domain.Catalog;

namespace Auraly.Platform.Application.Commerce;

public static class CartCommandOperations
{
    public const string Add = "add";
    public const string Remove = "remove";
    public const string CancelPending = "cancel_pending";
    public const string SetQuantity = "set_quantity";
}

public sealed record CartCommand(string Operation, string ProductText, decimal? Quantity, string? DestinationReference);

public sealed record ResolvedCartCommand(
    string Operation, ProductReference? Product, Guid? OrderItemId, decimal? Quantity, string ProductText);

public sealed record CartCommandCandidate(
    string Name,
    decimal UnitPrice,
    string Currency,
    Guid? ProductId = null,
    string? ExternalProductId = null,
    string? Sku = null,
    double? Score = null,
    bool IsAvailable = true);

public sealed record CartCommandIssue(string Code, string ProductText, IReadOnlyList<string> Candidates)
{
    public IReadOnlyList<CartCommandCandidate> ProductCandidates { get; init; } = [];
    public ProductResolutionStatus? ResolutionStatus { get; init; }
    public decimal? RequestedQuantity { get; init; }
    public decimal? AvailableQuantity { get; init; }
    public decimal? ExistingCartQuantity { get; init; }
    public decimal? MaximumCommandQuantity { get; init; }
}

public sealed record UnresolvedCartCommand(CartCommand Command, CartCommandIssue Issue);

public sealed record CartMutationApplyResult(OrderSnapshot Snapshot, bool Replayed);
public sealed record CartCommandBatchResult(
    bool Success,
    string Code,
    OrderSnapshot? Snapshot,
    IReadOnlyList<CartCommandIssue> Issues)
{
    public IReadOnlyList<ResolvedCartCommand> AppliedCommands { get; init; } = [];
    public IReadOnlyList<CartCommand> UnresolvedCommands { get; init; } = [];
    public IReadOnlyList<UnresolvedCartCommand> UnresolvedItems { get; init; } = [];
    public bool Replayed { get; init; }
}

public interface ICartProductResolver
{
    Task<IReadOnlyList<ProductReference>> FindAsync(
        AgentConversationContext context, string productText, CancellationToken cancellationToken = default);

    async Task<ProductResolution> ResolveAsync(
        AgentConversationContext context, string productText, CancellationToken cancellationToken = default) =>
        ProductResolutionEngine.Resolve(
            productText,
            (await FindAsync(context, productText, cancellationToken))
                .Select(product => new RetrievedProductCandidate(product, ProductMatchSource.Catalog))
                .ToList());
}

public interface ICartMutationStore
{
    Task<OrderSnapshot> GetCurrentAsync(AgentConversationContext context, CancellationToken cancellationToken = default);
    Task<OrderSnapshot> ApplyAtomicallyAsync(
        AgentConversationContext context,
        IReadOnlyList<ResolvedCartCommand> commands,
        CancellationToken cancellationToken = default);

    async Task<CartMutationApplyResult> ApplyIdempotentlyAsync(
        AgentConversationContext context,
        IReadOnlyList<ResolvedCartCommand> commands,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        new(await ApplyAtomicallyAsync(context, commands, cancellationToken), false);
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

    public Task<CartCommandBatchResult> ApplyAsync(
        AgentConversationContext context,
        IReadOnlyList<CartCommand> commands,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(context, commands, null, cancellationToken);

    public async Task<CartCommandBatchResult> ApplyAsync(
        AgentConversationContext context, IReadOnlyList<CartCommand> commands,
        string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0)
            return new(true, "cart.no_changes", await _store.GetCurrentAsync(context, cancellationToken), []);

        var batchFailure = ValidateBatch(context, commands);
        if (batchFailure is not null)
            return batchFailure;

        var current = await _store.GetCurrentAsync(context, cancellationToken);
        var resolved = new List<ResolvedCartCommand>(commands.Count);
        var issues = new List<CartCommandIssue>();
        var unresolved = new List<CartCommand>();
        var unresolvedItems = new List<UnresolvedCartCommand>();

        foreach (var command in commands)
        {
            var attempt = await ResolveCommandAsync(context, current, command, cancellationToken);
            if (attempt.Resolved is not null)
            {
                resolved.Add(attempt.Resolved);
                continue;
            }
            issues.Add(attempt.Issue!);
            unresolvedItems.Add(new(command, attempt.Issue!));
            unresolved.Add(command);
        }

        var stockIssues = FindStockIssues(current, resolved);
        if (stockIssues.Count > 0)
        {
            var blockedKeys = stockIssues.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var blocked = resolved.Where(command => command.Product is not null
                && blockedKeys.Contains(ProductKey(command.Product))).ToList();
            resolved.RemoveAll(command => command.Product is not null
                && blockedKeys.Contains(ProductKey(command.Product)));
            foreach (var command in blocked)
            {
                unresolved.Add(new CartCommand(
                    command.Operation, command.ProductText, command.Quantity, null));
                unresolvedItems.Add(new(unresolved[^1], stockIssues[ProductKey(command.Product!)]));
            }
            issues.AddRange(stockIssues.Values);
        }

        var mutation = resolved.Count > 0
            ? await _store.ApplyIdempotentlyAsync(context, resolved, idempotencyKey, cancellationToken)
            : new CartMutationApplyResult(current, false);
        var snapshot = mutation.Snapshot;
        if (issues.Count == 0)
        {
            return new CartCommandBatchResult(true, "cart.applied", snapshot, [])
            {
                AppliedCommands = resolved,
                Replayed = mutation.Replayed
            };
        }

        var code = resolved.Count > 0 ? "cart.partially_applied" : ToOutcomeCode(issues[0].Code);
        return new CartCommandBatchResult(false, code, snapshot, issues)
        {
            AppliedCommands = resolved,
            UnresolvedCommands = unresolved,
            UnresolvedItems = unresolvedItems,
            Replayed = mutation.Replayed
        };
    }

    private async Task<CommandResolutionAttempt> ResolveCommandAsync(
        AgentConversationContext context,
        OrderSnapshot current,
        CartCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Operation.Equals(CartCommandOperations.Add, StringComparison.OrdinalIgnoreCase))
            return FromProductResolution(command, await _products.ResolveAsync(context, command.ProductText, cancellationToken));

        var item = SelectSingleItem(command.ProductText, current.Items);
        if (item is null && command.Operation.Equals(CartCommandOperations.SetQuantity, StringComparison.OrdinalIgnoreCase))
        {
            var resolution = await _products.ResolveAsync(context, command.ProductText, cancellationToken);
            if (resolution.Status == ProductResolutionStatus.Resolved)
            {
                return new(new ResolvedCartCommand(
                    CartCommandOperations.Add, resolution.Selected, null, command.Quantity, command.ProductText), null);
            }
            return FromProductResolution(command, resolution);
        }

        if (item is null)
        {
            var candidates = current.Items.Select(value => value.ProductName).ToList();
            return new(null, new CartCommandIssue("item_not_found_or_ambiguous", command.ProductText, candidates));
        }

        ProductReference? quantityProduct = null;
        if (command.Operation == CartCommandOperations.SetQuantity && command.Quantity > item.Quantity)
        {
            var resolution = await _products.ResolveAsync(context, command.ProductText, cancellationToken);
            if (resolution.Status != ProductResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(item.Sku))
                resolution = await _products.ResolveAsync(context, item.Sku, cancellationToken);
            if (resolution.Status != ProductResolutionStatus.Resolved)
                resolution = await _products.ResolveAsync(context, item.ProductName, cancellationToken);
            if (resolution.Status != ProductResolutionStatus.Resolved)
                return FromProductResolution(command, resolution);
            quantityProduct = resolution.Selected;
        }

        return new(new ResolvedCartCommand(
            command.Operation, quantityProduct, item.OrderItemId, command.Quantity, command.ProductText), null);
    }

    private static CommandResolutionAttempt FromProductResolution(CartCommand command, ProductResolution resolution)
    {
        if (resolution.Status == ProductResolutionStatus.Resolved && resolution.Selected is not null)
        {
            return new(new ResolvedCartCommand(
                command.Operation, resolution.Selected, null, command.Quantity, command.ProductText), null);
        }

        var code = resolution.Status switch
        {
            ProductResolutionStatus.SuggestionRequired => "product_suggestion",
            ProductResolutionStatus.Ambiguous => "product_ambiguous",
            ProductResolutionStatus.Unavailable => "product_unavailable",
            _ => "product_not_found"
        };
        var candidates = resolution.Candidates.Select(candidate => candidate.Product.Name).ToList();
        return new(null, new CartCommandIssue(code, command.ProductText, candidates)
        {
            ResolutionStatus = resolution.Status,
            ProductCandidates = resolution.Candidates.Select(candidate => new CartCommandCandidate(
                candidate.Product.Name,
                candidate.Product.EffectiveUnitPrice ?? candidate.Product.UnitPrice,
                candidate.Product.Currency,
                candidate.Product.ProductId,
                candidate.Product.ExternalProductId,
                candidate.Product.Sku,
                candidate.Score,
                candidate.Product.IsActive)).ToList()
        });
    }

    private static CartCommandBatchResult? ValidateBatch(
        AgentConversationContext context,
        IReadOnlyList<CartCommand> commands)
    {
        var destinations = commands.Select(command => command.DestinationReference?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (destinations.Count > 1
            && (!context.Facts.TryGetValue("delivery_address", out var selectedDestination)
                || string.IsNullOrWhiteSpace(selectedDestination)))
        {
            return new(false, "cart.multiple_destinations", null,
                [new CartCommandIssue("multiple_destinations", string.Join(" | ", destinations), destinations!)]);
        }

        foreach (var command in commands)
        {
            if (command.Operation is not (CartCommandOperations.Add or CartCommandOperations.Remove or CartCommandOperations.SetQuantity)
                || string.IsNullOrWhiteSpace(command.ProductText)
                || command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity && command.Quantity is null or <= 0)
            {
                return Failure("cart.invalid_input", command.ProductText);
            }
        }

        var duplicate = commands.GroupBy(command => Normalize(command.ProductText), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicate is null ? null : Failure("cart.conflicting_commands", duplicate.First().ProductText);
    }

    private static CartCommandBatchResult Failure(string code, string productText) =>
        new(false, code, null, [new CartCommandIssue(code.Replace("cart.", string.Empty, StringComparison.Ordinal), productText, [])]);

    private static Dictionary<string, CartCommandIssue> FindStockIssues(
        OrderSnapshot current,
        IReadOnlyList<ResolvedCartCommand> commands)
    {
        var result = new Dictionary<string, CartCommandIssue>(StringComparer.OrdinalIgnoreCase);
        var groups = commands.Where(command => command.Product?.StockQuantity is not null)
            .GroupBy(command => ProductKey(command.Product!), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var product = group.First().Product!;
            var existingQuantity = current.Items.Where(item => IsSameProduct(item, product)).Sum(item => item.Quantity);
            var setQuantity = group.LastOrDefault(command => command.Operation == CartCommandOperations.SetQuantity);
            var requestedQuantity = setQuantity is not null
                ? setQuantity.Quantity ?? existingQuantity
                : existingQuantity + group.Sum(command => command.Quantity ?? 0m);
            if (requestedQuantity <= product.StockQuantity!.Value)
                continue;
            result[group.Key] = new CartCommandIssue("insufficient_stock", product.Name, [])
            {
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = product.StockQuantity.Value,
                ExistingCartQuantity = existingQuantity,
                MaximumCommandQuantity = setQuantity is not null
                    ? product.StockQuantity.Value
                    : Math.Max(product.StockQuantity.Value - existingQuantity, 0m)
            };
        }
        return result;
    }

    private static OrderItemSnapshot? SelectSingleItem(string text, IReadOnlyList<OrderItemSnapshot> items)
    {
        var normalized = Normalize(text);
        var exact = items.Where(item => Normalize(item.ProductName) == normalized || Normalize(item.Sku) == normalized).ToList();
        if (exact.Count == 1)
            return exact[0];
        var terms = ProductSearchText.GetTokens(text).ToHashSet(StringComparer.Ordinal);
        var partial = items.Where(item =>
            Normalize(item.ProductName).Contains(normalized, StringComparison.Ordinal)
            || terms.IsSubsetOf(ProductSearchText.GetTokens($"{item.ProductName} {item.Sku}").ToHashSet(StringComparer.Ordinal)))
            .ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private static bool IsSameProduct(OrderItemSnapshot item, ProductReference product) =>
        product.ProductId.HasValue && item.ProductId == product.ProductId
        || !string.IsNullOrWhiteSpace(product.ExternalProductId) && item.ExternalProductId?.Equals(product.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
        || !string.IsNullOrWhiteSpace(product.Sku) && item.Sku?.Equals(product.Sku, StringComparison.OrdinalIgnoreCase) == true
        || Normalize(item.ProductName) == Normalize(product.Name);

    private static string ProductKey(ProductReference product) =>
        product.ProductId?.ToString("N") ?? product.ExternalProductId?.Trim().ToLowerInvariant()
        ?? product.Sku?.Trim().ToLowerInvariant() ?? Normalize(product.Name);

    private static string ToOutcomeCode(string issueCode) => issueCode switch
    {
        "product_suggestion" => "cart.product_suggestion",
        "product_ambiguous" => "cart.product_ambiguous",
        "product_not_found" => "cart.product_not_found",
        "product_unavailable" => "cart.product_unavailable",
        "insufficient_stock" => "cart.insufficient_stock",
        "item_not_found_or_ambiguous" => "cart.item_not_found_or_ambiguous",
        _ => "cart.needs_clarification"
    };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit).ToArray()).Normalize(NormalizationForm.FormC);
    }

    private sealed record CommandResolutionAttempt(ResolvedCartCommand? Resolved, CartCommandIssue? Issue);
}
