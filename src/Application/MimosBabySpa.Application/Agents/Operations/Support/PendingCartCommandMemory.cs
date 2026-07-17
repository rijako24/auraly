using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Agents.Operations.Support;

internal static class PendingCartCommandMemory
{
    internal const string FactKey = "system.pending_cart_commands";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PendingCartMergeResult MergeResolution(
        AgentConversationContext context,
        IReadOnlyList<CartCommand> incoming)
    {
        var pending = Read(context);
        if (pending is null)
        {
            return new(
                incoming.Select(command => new PendingCartWorkItem(command, command.ProductText)).ToList(),
                [], [], false);
        }

        var remainingIncoming = incoming.ToList();
        var work = new List<PendingCartWorkItem>();
        var remaining = new List<PendingCartItem>();
        var confirmations = new List<PendingAliasConfirmation>();
        var cancelledAny = false;
        var latestMessage = context.LatestUserMessage ?? string.Empty;

        foreach (var completed in pending.Items.Where(item => item.AlreadyApplied))
        {
            var related = remainingIncoming.FirstOrDefault(command =>
                command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                && (SameReference(command.ProductText, completed.Command.ProductText)
                    || SameReference(command.ProductText, completed.OriginalProductText)
                    || IsPendingRefinement(completed.OriginalProductText, command.ProductText)));
            if (related is not null && !IsExplicitAdditionalRequest(latestMessage, related.ProductText))
            {
                if (related.Operation == CartCommandOperations.SetQuantity
                    || related.Quantity is > 1m && related.Quantity != completed.Command.Quantity)
                {
                    var replacement = related with
                    {
                        Operation = CartCommandOperations.SetQuantity,
                        ProductText = completed.Command.ProductText
                    };
                    work.Add(new(replacement, completed.OriginalProductText));
                }
                remainingIncoming.Remove(related);
                continue;
            }
            var replay = remainingIncoming.FirstOrDefault(command =>
                command.Operation.Equals(completed.Command.Operation, StringComparison.OrdinalIgnoreCase)
                && command.Quantity == completed.Command.Quantity
                && (SameReference(command.ProductText, completed.Command.ProductText)
                    || IsPlausibleRefinement(completed.Command.ProductText, command.ProductText)));
            if (replay is not null && !IsExplicitAdditionalRequest(latestMessage, replay.ProductText))
                remainingIncoming.Remove(replay);
        }


        foreach (var item in pending.Items)
        {
            if (item.AlreadyApplied)
                continue;

            if (!item.RequiresResolution)
            {
                work.Add(new(item.Command, item.OriginalProductText));
                continue;
            }

            var cancellation = remainingIncoming.FirstOrDefault(command =>
                command.Operation.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase)
                && SameReference(command.ProductText, item.OriginalProductText));
            if (cancellation is not null)
            {
                remainingIncoming.Remove(cancellation);
                cancelledAny = true;
                continue;
            }

            var candidates = item.Issue?.ProductCandidates ?? [];
            var relatedIncoming = remainingIncoming.FirstOrDefault(command =>
                command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                && (SameReference(command.ProductText, item.Command.ProductText)
                    || SameReference(command.ProductText, item.OriginalProductText)
                    || SameReference(command.ProductText, item.Issue?.ProductText ?? string.Empty)
                    || IsPendingRefinement(item.Command.ProductText, command.ProductText)
                    || IsPendingRefinement(item.OriginalProductText, command.ProductText)
                    || IsPendingRefinement(item.Issue?.ProductText ?? string.Empty, command.ProductText)));

            if (relatedIncoming is not null
                && item.Issue?.Code == "insufficient_stock"
                && !string.IsNullOrWhiteSpace(item.Issue.ProductText))
            {
                var replacement = item.Command with { ProductText = item.Issue.ProductText };
                if (relatedIncoming.Operation == CartCommandOperations.SetQuantity
                    || relatedIncoming.Quantity is > 1m && relatedIncoming.Quantity != item.Command.Quantity
                    || HasExplicitQuantityForReference(latestMessage, relatedIncoming))
                    replacement = replacement with { Quantity = relatedIncoming.Quantity };
                work.Add(new(replacement, item.OriginalProductText));
                remainingIncoming.Remove(relatedIncoming);
                continue;
            }

            var latestMatches = candidates.Where(candidate => IsReferenceMatch(latestMessage, candidate.Name)).ToList();
            var selectionAttempts = remainingIncoming
                .Select(command => new
                {
                    Command = command,
                    Matches = candidates.Where(candidate => IsReferenceMatch(command.ProductText, candidate.Name)).ToList()
                })
                .ToList();
            var incomingSelection = selectionAttempts.FirstOrDefault(value => value.Matches.Count == 1
                && (candidates.Count == 1
                    || HasGroundedCandidateDiscriminator(latestMessage, value.Matches[0], candidates)));
            var ungroundedSelection = selectionAttempts.FirstOrDefault(value => value.Matches.Count == 1
                && candidates.Count > 1
                && !HasGroundedCandidateDiscriminator(latestMessage, value.Matches[0], candidates));
            var selected = latestMatches.Count == 1
                ? latestMatches[0]
                : incomingSelection?.Matches.Count == 1 ? incomingSelection.Matches[0] : null;
            if (selected is not null)
            {
                var resolvingCommand = incomingSelection?.Command
                    ?? remainingIncoming.FirstOrDefault(command =>
                        SameReference(command.ProductText, item.Command.ProductText)
                        || IsPlausibleRefinement(item.Command.ProductText, command.ProductText));
                var replacement = item.Command with { ProductText = selected.Name };
                if (resolvingCommand is not null && (resolvingCommand.Operation == CartCommandOperations.SetQuantity
                    || resolvingCommand.Quantity is > 1m && resolvingCommand.Quantity != item.Command.Quantity
                    || HasExplicitQuantityForReference(latestMessage, resolvingCommand)))
                    replacement = replacement with { Quantity = resolvingCommand.Quantity };
                work.Add(new(replacement, item.OriginalProductText));
                confirmations.Add(new(item.OriginalProductText, selected.Name));
                if (resolvingCommand is not null)
                    remainingIncoming.Remove(resolvingCommand);
                continue;
            }
            if (ungroundedSelection is not null)
            {
                remainingIncoming.Remove(ungroundedSelection.Command);
                remaining.Add(item);
                continue;
            }


            if (candidates.Count == 0)
            {
                var catalogMatches = ProductSelectionMemory.FindCatalogMatches(context, latestMessage)
                    .Where(product => IsPlausibleRefinement(item.Command.ProductText, product.Name))
                    .ToList();
                if (catalogMatches.Count == 1)
                {
                    var selectedProduct = catalogMatches[0];
                    work.Add(new(item.Command with { ProductText = selectedProduct.Name }, item.OriginalProductText));
                    var continuation = remainingIncoming.FirstOrDefault(command =>
                        SameReference(command.ProductText, item.Command.ProductText)
                        || IsPlausibleRefinement(item.Command.ProductText, command.ProductText));
                    if (continuation is not null)
                        remainingIncoming.Remove(continuation);
                    confirmations.Add(new(item.OriginalProductText, selectedProduct.Name));
                    continue;
                }


                var refinement = remainingIncoming.FirstOrDefault(command =>
                    command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                    && IsPlausibleRefinement(item.Command.ProductText, command.ProductText));
                if (refinement is not null)
                {
                    var replacement = item.Command with { ProductText = refinement.ProductText };
                    if (HasExplicitQuantityForReference(latestMessage, refinement))
                        replacement = replacement with { Quantity = refinement.Quantity };
                    work.Add(new(replacement, item.OriginalProductText));
                    remainingIncoming.Remove(refinement);
                    continue;
                }
            }

            if (relatedIncoming is not null)
            {
                var replacement = item.Command with { ProductText = relatedIncoming.ProductText };
                if (relatedIncoming.Operation == CartCommandOperations.SetQuantity
                    || relatedIncoming.Quantity is > 1m && relatedIncoming.Quantity != item.Command.Quantity
                    || HasExplicitQuantityForReference(latestMessage, relatedIncoming))
                    replacement = replacement with { Quantity = relatedIncoming.Quantity };
                work.Add(new(replacement, item.OriginalProductText));
                remainingIncoming.Remove(relatedIncoming);
                continue;
            }

            remaining.Add(item);
        }

        work.AddRange(remainingIncoming
            .Where(command => !command.Operation.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase))
            .Select(command => new PendingCartWorkItem(command, command.ProductText)));
        return new(work, remaining, confirmations, cancelledAny);
    }

    public static async Task SaveAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        IReadOnlyList<PendingCartItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            await ClearAsync(facts, context, cancellationToken);
            return;
        }

        var pending = new PendingCartCommandBatch(2, items, DateTime.UtcNow.AddMinutes(30));
        var json = JsonSerializer.Serialize(pending, JsonOptions);
        await facts.SetAsync(context.ConversationId, context.BusinessId, FactKey, json,
            rememberAcrossRequests: false, cancellationToken);
        context.Facts[FactKey] = json;
    }

    public static async Task ClearAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        CancellationToken cancellationToken)
    {
        await facts.ClearFieldsAsync(context.ConversationId, [FactKey], cancellationToken);
        context.Facts.Remove(FactKey);
    }

    public static PendingCartCommandBatch? Read(AgentConversationContext context) => Read(context.Facts);

    public static PendingCartCommandBatch? Read(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var version = root.TryGetProperty("schemaVersion", out var versionElement) ? versionElement.GetInt32() : 1;
            if (version >= 2)
            {
                var current = JsonSerializer.Deserialize<PendingCartCommandBatch>(raw, JsonOptions);
                return current?.ExpiresAtUtc > DateTime.UtcNow ? current : null;
            }

            var legacy = JsonSerializer.Deserialize<LegacyPendingCartCommandBatch>(raw, JsonOptions);
            if (legacy is null || legacy.ExpiresAtUtc <= DateTime.UtcNow)
                return null;
            var candidates = legacy.ProductCandidates ?? [];
            var issue = new CartCommandIssue(
                candidates.Count == 0 ? "product_not_found" : "product_ambiguous",
                legacy.AmbiguousProductText,
                candidates.Select(candidate => candidate.Name).ToList())
            {
                ResolutionStatus = candidates.Count == 0 ? ProductResolutionStatus.NotFound : ProductResolutionStatus.Ambiguous,
                ProductCandidates = candidates
            };
            var items = legacy.Commands.Select(command =>
                SameReference(command.ProductText, legacy.AmbiguousProductText)
                    ? new PendingCartItem(command, command.ProductText, issue, true)
                    : new PendingCartItem(command, command.ProductText, null, false)).ToList();
            return new(2, items, legacy.ExpiresAtUtc);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CartCommandIssue PrimaryIssue(IReadOnlyList<PendingCartItem> items) =>
        items.Select(item => item.Issue).FirstOrDefault(issue => issue is not null)
        ?? new CartCommandIssue("product_not_found", items.FirstOrDefault()?.OriginalProductText ?? string.Empty, []);

    private static bool IsPlausibleRefinement(string previous, string incoming)
    {
        var previousTokens = ProductSearchText.GetTokens(previous);
        var incomingTokens = ProductSearchText.GetTokens(incoming);
        if (previousTokens.Count == 0 || incomingTokens.Count == 0)
            return false;
        return previousTokens.Any(left => incomingTokens.Any(right =>
            left.Equals(right, StringComparison.Ordinal)
            || ProductSearchText.TokenSimilarity(left, right) >= 0.78d));
    }

    private static bool IsPendingRefinement(string previous, string incoming)
    {
        var previousTokens = ProductSearchText.GetMatchingTokens(previous).ToHashSet(StringComparer.Ordinal);
        if (previousTokens.Count == 0)
            return false;
        return ProductSearchText.GetMatchingTokens(incoming)
            .Any(previousTokens.Contains);
    }

    private static bool HasGroundedCandidateDiscriminator(
        string message,
        CartCommandCandidate selected,
        IReadOnlyList<CartCommandCandidate> candidates)
    {
        var messageTokens = ProductSearchText.GetMatchingTokens(message);
        var selectedTokens = ProductSearchText.GetMatchingTokens(selected.Name);
        var otherTokenSets = candidates
            .Where(candidate => candidate != selected)
            .Select(candidate => ProductSearchText.GetMatchingTokens(candidate.Name))
            .ToList();
        var distinctive = selectedTokens.Where(token => otherTokenSets.All(other =>
            !other.Any(otherToken => token == otherToken
                || ProductSearchText.TokenSimilarity(token, otherToken) >= 0.8d)));
        if (distinctive.Any(token => messageTokens.Any(messageToken =>
            token == messageToken
            || !token.All(char.IsDigit) && !messageToken.All(char.IsDigit)
                && ProductSearchText.TokenSimilarity(token, messageToken) >= 0.6d)))
            return true;

        var normalized = ProductSearchText.NormalizeWords(message);
        return messageTokens.Count <= 8 && Regex.IsMatch(normalized,
            @"\b(esta|esa|primera|primero|segunda|segundo|tercera|tercero|ultima|ultimo)\b",
            RegexOptions.CultureInvariant);
    }

    private static bool HasExplicitQuantityForReference(string message, CartCommand command)
    {
        if (command.Quantity is not { } quantity || string.IsNullOrWhiteSpace(message))
            return false;
        var referenceTerms = ProductSearchText.GetTokens(command.ProductText).Where(term => term.Length >= 3).ToHashSet(StringComparer.Ordinal);
        if (referenceTerms.Count == 0)
            return false;
        var clauses = Regex.Split(message, @"(?<!\d),(?!\d)|;|\b(?:y|e|tambien|ademas)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return clauses.Any(clause =>
            referenceTerms.Overlaps(ProductSearchText.GetTokens(clause)) && ContainsExplicitQuantity(clause, quantity));
    }

    private static bool ContainsExplicitQuantity(string clause, decimal expected)
    {
        foreach (Match match in Regex.Matches(clause, @"(?<![\p{L}\d])\d+(?:[.,]\d+)?(?![\p{L}\d])", RegexOptions.CultureInvariant))
        {
            if (decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                && parsed == expected)
                return true;
        }
        return ProductSearchText.GetTokens(clause).Any(token => TryReadQuantityWord(token, out var value) && value == expected);
    }

    private static bool TryReadQuantityWord(string token, out decimal value)
    {
        value = token switch
        {
            "un" or "una" or "uno" => 1m, "dos" => 2m, "tres" => 3m, "cuatro" => 4m,
            "cinco" => 5m, "seis" => 6m, "siete" => 7m, "ocho" => 8m, "nueve" => 9m,
            "diez" => 10m, "once" => 11m, "doce" => 12m, "trece" => 13m, "catorce" => 14m,
            "quince" => 15m, "dieciseis" => 16m, "diecisiete" => 17m, "dieciocho" => 18m,
            "diecinueve" => 19m, "veinte" => 20m, _ => 0m
        };
        return value > 0;
    }

    private static bool IsReferenceMatch(string reference, string candidate)
    {
        var tokens = ProductSearchText.GetTokens(reference);
        var candidateTokens = ProductSearchText.GetTokens(candidate);
        return tokens.Count > 0 && tokens.All(token => candidateTokens.Any(candidateToken =>
            token == candidateToken || ProductSearchText.TokenSimilarity(token, candidateToken) >= 0.6d));
    }
    private static bool IsExplicitAdditionalRequest(string message, string productText)
    {
        var normalized = ProductSearchText.NormalizeWords(message);
        if (!IsPlausibleRefinement(message, productText))
            return false;
        return Regex.IsMatch(normalized,
            @"\b(otra|otro|adicional|adicionales|mas|nuevamente)\b|\btambien\s+(?:agrega|agregame|anade)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }


    private static bool SameReference(string left, string right) =>
        CatalogSearchText.NormalizeCompact(left) == CatalogSearchText.NormalizeCompact(right);
}

internal sealed record PendingCartMergeResult(
    IReadOnlyList<PendingCartWorkItem> WorkItems,
    IReadOnlyList<PendingCartItem> RemainingItems,
    IReadOnlyList<PendingAliasConfirmation> Confirmations,
    bool CancelledAny);

internal sealed record PendingCartWorkItem(CartCommand Command, string OriginalProductText);
internal sealed record PendingAliasConfirmation(string OriginalProductText, string SelectedProductName);
internal sealed record PendingCartItem(CartCommand Command, string OriginalProductText, CartCommandIssue? Issue, bool RequiresResolution, bool AlreadyApplied = false);
internal sealed record PendingCartCommandBatch(int SchemaVersion, IReadOnlyList<PendingCartItem> Items, DateTime ExpiresAtUtc)
{
    public IReadOnlyList<CartCommand> Commands => Items.Select(item => item.Command).ToList();
    public string AmbiguousProductText => Items.FirstOrDefault(item => item.RequiresResolution)?.OriginalProductText ?? string.Empty;
    public IReadOnlyList<CartCommandCandidate> ProductCandidates =>
        Items.FirstOrDefault(item => item.RequiresResolution)?.Issue?.ProductCandidates ?? [];
}

internal sealed record LegacyPendingCartCommandBatch(
    int SchemaVersion,
    IReadOnlyList<CartCommand> Commands,
    string AmbiguousProductText,
    IReadOnlyList<CartCommandCandidate>? ProductCandidates,
    DateTime ExpiresAtUtc);
