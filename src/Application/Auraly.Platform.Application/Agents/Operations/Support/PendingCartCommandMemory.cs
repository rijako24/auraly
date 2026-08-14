using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Catalog;

namespace Auraly.Platform.Application.Agents.Operations.Support;

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
        var conversationPolicy = context.Config?.Commerce.Conversation ?? new CommerceConversationPolicy();
        var matchingPolicy = context.Config?.Commerce.Matching ?? new ProductMatchingPolicy();
        var confirmedCandidate = FindConfirmedCandidate(
            context, pending, latestMessage, conversationPolicy, matchingPolicy);

        foreach (var completed in pending.Items.Where(item => item.AlreadyApplied))
        {
            var related = remainingIncoming.FirstOrDefault(command =>
                command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                && (SameReference(command.ProductText, completed.Command.ProductText)
                    || SameReference(command.ProductText, completed.OriginalProductText)
                    || IsPendingRefinement(completed.OriginalProductText, command.ProductText)));
            if (related is not null && !IsExplicitAdditionalRequest(latestMessage, related.ProductText, conversationPolicy, matchingPolicy))
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
                    || IsPlausibleRefinement(completed.Command.ProductText, command.ProductText, matchingPolicy)));
            if (replay is not null && !IsExplicitAdditionalRequest(latestMessage, replay.ProductText, conversationPolicy, matchingPolicy))
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
            var previousTurnSelection = confirmedCandidate is null
                ? null
                : candidates.FirstOrDefault(candidate => SameReference(candidate.Name, confirmedCandidate.Name));
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
                    || HasExplicitQuantityForReference(latestMessage, relatedIncoming, conversationPolicy))
                    replacement = replacement with { Quantity = relatedIncoming.Quantity };
                work.Add(new(replacement, item.OriginalProductText));
                remainingIncoming.Remove(relatedIncoming);
                continue;
            }

            var latestMatches = candidates
                .Where(candidate => IsReferenceMatch(latestMessage, candidate.Name, matchingPolicy))
                .ToList();
            var selectionAttempts = remainingIncoming
                .Select(command => new
                {
                    Command = command,
                    Matches = candidates
                        .Where(candidate => IsReferenceMatch(command.ProductText, candidate.Name, matchingPolicy))
                        .ToList()
                })
                .ToList();
            var incomingSelection = selectionAttempts.FirstOrDefault(value => value.Matches.Count == 1
                && (candidates.Count == 1
                    || HasGroundedCandidateDiscriminator(latestMessage, value.Matches[0], candidates, conversationPolicy, matchingPolicy)));
            var ungroundedSelection = selectionAttempts.FirstOrDefault(value => value.Matches.Count == 1
                && candidates.Count > 1
                && !HasGroundedCandidateDiscriminator(latestMessage, value.Matches[0], candidates, conversationPolicy, matchingPolicy));
            var selected = previousTurnSelection ?? (latestMatches.Count == 1
                ? latestMatches[0]
                : incomingSelection?.Matches.Count == 1 ? incomingSelection.Matches[0] : null);
            if (selected is not null)
            {
                var resolvingCommand = incomingSelection?.Command
                    ?? remainingIncoming.FirstOrDefault(command =>
                        SameReference(command.ProductText, item.Command.ProductText)
                        || IsPlausibleRefinement(item.Command.ProductText, command.ProductText, matchingPolicy));
                var replacement = item.Command with { ProductText = selected.Name };
                if (resolvingCommand is not null && (resolvingCommand.Operation == CartCommandOperations.SetQuantity
                    || resolvingCommand.Quantity is > 1m && resolvingCommand.Quantity != item.Command.Quantity
                    || HasExplicitQuantityForReference(latestMessage, resolvingCommand, conversationPolicy)))
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
                var unresolvedItem = item;
                if (HasExplicitQuantityForReference(latestMessage, ungroundedSelection.Command, conversationPolicy))
                    unresolvedItem = item with
                    {
                        Command = item.Command with { Quantity = ungroundedSelection.Command.Quantity }
                    };
                remaining.Add(unresolvedItem);
                continue;
            }


            if (candidates.Count == 0)
            {
                var catalogMatches = ProductSelectionMemory.FindCatalogMatches(context, latestMessage)
                    .Where(product => IsPlausibleRefinement(item.Command.ProductText, product.Name, matchingPolicy))
                    .ToList();
                if (catalogMatches.Count == 1)
                {
                    var selectedProduct = catalogMatches[0];
                    work.Add(new(item.Command with { ProductText = selectedProduct.Name }, item.OriginalProductText));
                    var continuation = remainingIncoming.FirstOrDefault(command =>
                        SameReference(command.ProductText, item.Command.ProductText)
                        || IsPlausibleRefinement(item.Command.ProductText, command.ProductText, matchingPolicy));
                    if (continuation is not null)
                        remainingIncoming.Remove(continuation);
                    confirmations.Add(new(item.OriginalProductText, selectedProduct.Name));
                    continue;
                }


                var refinement = remainingIncoming.FirstOrDefault(command =>
                    command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                    && IsPlausibleRefinement(item.Command.ProductText, command.ProductText, matchingPolicy));
                if (refinement is not null)
                {
                    var replacement = item.Command with { ProductText = refinement.ProductText };
                    if (HasExplicitQuantityForReference(latestMessage, refinement, conversationPolicy))
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
                    || HasExplicitQuantityForReference(latestMessage, relatedIncoming, conversationPolicy))
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

    public static CartCommandIssue PrimaryIssue(
        IReadOnlyList<PendingCartItem> items,
        string? latestMessage = null,
        ProductMatchingPolicy? matchingPolicy = null) =>
        items
            .Where(item => item.Issue is not null)
            .OrderByDescending(item => PendingMatchScore(
                item, latestMessage ?? string.Empty, matchingPolicy ?? new ProductMatchingPolicy()))
            .Select(item => item.Issue!)
            .FirstOrDefault()
        ?? new CartCommandIssue("product_not_found", items.FirstOrDefault()?.OriginalProductText ?? string.Empty, []);

    public static PendingCartItem? FindReferencedItem(
        IReadOnlyList<PendingCartItem> items,
        string? message,
        ProductMatchingPolicy? matchingPolicy = null)
    {
        var policy = matchingPolicy ?? new ProductMatchingPolicy();
        var matches = items
            .Where(item => item.RequiresResolution)
            .Select(item => new
            {
                Item = item,
                Score = PendingMatchScore(item, message ?? string.Empty, policy)
            })
            .Where(match => match.Score > 0)
            .ToList();
        if (matches.Count == 0)
            return null;

        var maximumScore = matches.Max(match => match.Score);
        var bestMatches = matches
            .Where(match => match.Score == maximumScore)
            .Select(match => match.Item)
            .ToList();
        return bestMatches.Count == 1 ? bestMatches[0] : null;
    }

    public static PendingCartItem? FindUniquelyReferencedItem(
        IReadOnlyList<PendingCartItem> items,
        string? message,
        ProductMatchingPolicy? matchingPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var policy = matchingPolicy ?? new ProductMatchingPolicy();
        var matches = items.Where(item => item.RequiresResolution
            && new[]
            {
                item.OriginalProductText,
                item.Command.ProductText,
                item.Issue?.ProductText ?? string.Empty
            }
            .Concat(item.Issue?.ProductCandidates.Select(candidate => candidate.Name) ?? [])
            .Any(reference => IsCandidateExplicitlyMentioned(message, reference, policy)))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
    public static bool TryReadSingleQuantity(
        string? message,
        CommerceConversationPolicy? conversationPolicy,
        out decimal quantity)
    {
        var values = new HashSet<decimal>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            foreach (Match match in Regex.Matches(
                message,
                @"(?<![\p{L}\d])\d+(?:[.,]\d+)?(?![\p{L}\d])",
                RegexOptions.CultureInvariant))
            {
                if (decimal.TryParse(
                        match.Value.Replace(',', '.'),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    && parsed > 0m)
                    values.Add(parsed);
            }

            if (conversationPolicy is not null)
            {
                foreach (var token in ProductSearchText.GetTokens(message))
                {
                    if (TryReadQuantityWord(token, conversationPolicy, out var parsed)
                        && parsed > 0m)
                        values.Add(parsed);
                }
            }
        }

        quantity = values.Count == 1 ? values.Single() : 0m;
        return values.Count == 1;
    }

    internal static bool IsContextualConfirmation(
        string message,
        CommerceConversationPolicy? conversationPolicy) =>
        CommerceConversationMatcher.IsExactPhrase(
            message, conversationPolicy?.ContextualConfirmationPhrases);

    private static CartCommandCandidate? FindConfirmedCandidate(
        AgentConversationContext context,
        PendingCartCommandBatch pending,
        string latestMessage,
        CommerceConversationPolicy conversationPolicy,
        ProductMatchingPolicy matchingPolicy)
    {
        if (!IsContextualConfirmation(latestMessage, conversationPolicy))
            return null;
        var previousAssistantMessage = context.ConversationState?.LastBotMessage;
        if (string.IsNullOrWhiteSpace(previousAssistantMessage))
            return null;

        var matches = pending.Items
            .Where(item => item.RequiresResolution)
            .SelectMany(item => item.Issue?.ProductCandidates ?? [])
            .Where(candidate => IsCandidateExplicitlyMentioned(
                previousAssistantMessage, candidate.Name, matchingPolicy))
            .GroupBy(candidate => CatalogSearchText.NormalizeCompact(candidate.Name), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool IsCandidateExplicitlyMentioned(
        string message,
        string candidate,
        ProductMatchingPolicy matchingPolicy)
    {
        var messageTokens = ProductSearchText.GetMatchingTokens(message);
        var candidateTokens = ProductSearchText.GetMatchingTokens(candidate);
        return candidateTokens.Count > 0 && candidateTokens.All(token => messageTokens.Any(messageToken =>
            token == messageToken
            || ProductSearchText.TokenSimilarity(token, messageToken)
                >= matchingPolicy.CandidateMentionSimilarity));
    }

    private static int PendingMatchScore(
        PendingCartItem item,
        string message,
        ProductMatchingPolicy matchingPolicy)
    {
        var messageTokens = ProductSearchText.GetMatchingTokens(message);
        if (messageTokens.Count == 0)
            return 0;
        var references = new[]
            {
                item.OriginalProductText,
                item.Command.ProductText,
                item.Issue?.ProductText ?? string.Empty
            }
            .Concat(item.Issue?.ProductCandidates.Select(candidate => candidate.Name) ?? []);
        return references.Max(reference => ProductSearchText.GetMatchingTokens(reference).Sum(token =>
            messageTokens.Contains(token, StringComparer.Ordinal)
                ? 10
                : messageTokens.Any(messageToken => ProductSearchText.TokenSimilarity(token, messageToken)
                    >= matchingPolicy.PendingReferenceSimilarity)
                    ? 1
                    : 0));
    }

    private static bool IsPlausibleRefinement(
        string previous,
        string incoming,
        ProductMatchingPolicy matchingPolicy)
    {
        var previousTokens = ProductSearchText.GetTokens(previous);
        var incomingTokens = ProductSearchText.GetTokens(incoming);
        if (previousTokens.Count == 0 || incomingTokens.Count == 0)
            return false;
        return previousTokens.Any(left => incomingTokens.Any(right =>
            left.Equals(right, StringComparison.Ordinal)
            || ProductSearchText.TokenSimilarity(left, right)
                >= matchingPolicy.PendingReferenceSimilarity));
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
        IReadOnlyList<CartCommandCandidate> candidates,
        CommerceConversationPolicy conversationPolicy,
        ProductMatchingPolicy matchingPolicy)
    {
        var messageTokens = ProductSearchText.GetMatchingTokens(message);
        var selectedTokens = ProductSearchText.GetMatchingTokens(selected.Name);
        var otherTokenSets = candidates
            .Where(candidate => candidate != selected)
            .Select(candidate => ProductSearchText.GetMatchingTokens(candidate.Name))
            .ToList();
        var distinctive = selectedTokens.Where(token => otherTokenSets.All(other =>
            !other.Any(otherToken => token == otherToken
                || ProductSearchText.TokenSimilarity(token, otherToken)
                    >= matchingPolicy.CandidateMentionSimilarity)));
        if (distinctive.Any(token => messageTokens.Any(messageToken =>
            token == messageToken
            || !token.All(char.IsDigit) && !messageToken.All(char.IsDigit)
                && ProductSearchText.TokenSimilarity(token, messageToken)
                    >= matchingPolicy.CandidateSelectionSimilarity)))
            return true;

        return messageTokens.Count <= 8
            && CommerceConversationMatcher.ContainsPhrase(
                message, conversationPolicy.CandidateSelectionPhrases);
    }

    private static bool HasExplicitQuantityForReference(
        string message,
        CartCommand command,
        CommerceConversationPolicy conversationPolicy)
    {
        if (command.Quantity is not { } quantity || string.IsNullOrWhiteSpace(message))
            return false;
        var referenceTerms = ProductSearchText.GetTokens(command.ProductText)
            .Where(term => term.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);
        if (referenceTerms.Count == 0)
            return false;
        var clauses = CommerceConversationMatcher.SplitClauses(
            message, conversationPolicy.ClauseSeparators);
        return clauses.Any(clause =>
            referenceTerms.Overlaps(ProductSearchText.GetTokens(clause))
            && ContainsExplicitQuantity(clause, quantity, conversationPolicy));
    }

    private static bool ContainsExplicitQuantity(
        string clause,
        decimal expected,
        CommerceConversationPolicy conversationPolicy)
    {
        foreach (Match match in Regex.Matches(
            clause,
            @"(?<![\p{L}\d])\d+(?:[.,]\d+)?(?![\p{L}\d])",
            RegexOptions.CultureInvariant))
        {
            if (decimal.TryParse(
                    match.Value.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                && parsed == expected)
                return true;
        }
        return ProductSearchText.GetTokens(clause)
            .Any(token => TryReadQuantityWord(token, conversationPolicy, out var value)
                && value == expected);
    }

    private static bool TryReadQuantityWord(
        string token,
        CommerceConversationPolicy conversationPolicy,
        out decimal value)
    {
        var normalized = ProductSearchText.NormalizeWords(token);
        foreach (var quantityWord in conversationPolicy.QuantityWords)
        {
            if (!normalized.Equals(
                    ProductSearchText.NormalizeWords(quantityWord.Key),
                    StringComparison.Ordinal))
                continue;
            value = quantityWord.Value;
            return true;
        }
        value = 0m;
        return false;
    }

    private static bool IsReferenceMatch(
        string reference,
        string candidate,
        ProductMatchingPolicy matchingPolicy)
    {
        var tokens = ProductSearchText.GetTokens(reference);
        var candidateTokens = ProductSearchText.GetTokens(candidate);
        return tokens.Count > 0 && tokens.All(token => candidateTokens.Any(candidateToken =>
            token == candidateToken
            || ProductSearchText.TokenSimilarity(token, candidateToken)
                >= matchingPolicy.CandidateSelectionSimilarity));
    }

    private static bool IsExplicitAdditionalRequest(
        string message,
        string productText,
        CommerceConversationPolicy conversationPolicy,
        ProductMatchingPolicy matchingPolicy)
    {
        if (!IsPlausibleRefinement(message, productText, matchingPolicy))
            return false;
        return CommerceConversationMatcher.ContainsPhrase(
            message, conversationPolicy.AdditionalRequestPhrases);
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
