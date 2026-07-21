using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Operations.Support;

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
        AgentConversationContext ctx,
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

    public static Task RememberCatalogAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
        IReadOnlyList<ProductReference> products,
        IReadOnlyList<string> searchTerms,
        CancellationToken ct,
        string? replacementReference = null) =>
        CatalogOfferMemory.RememberAsync(
            factsService, ctx, products, searchTerms, ct, replacementReference);
    public static string NormalizeSearchReference(string productText)
    {
        var normalized = NormalizeSelectionReference(productText);
        var exclusionMarkers = new[]
        {
            " aclarando que no ", " pero que no ", " pero no ",
            " que no sea ", " que no fuera ", " no quiero que sea "
        };
        var exclusionIndex = exclusionMarkers
            .Select(marker => normalized.IndexOf(marker, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (exclusionIndex > 0)
            normalized = normalized[..exclusionIndex];

        return string.Join(' ', normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static IReadOnlyList<ProductReference> FindCatalogMatches(
        AgentConversationContext ctx,
        string productText)
    {
        var memory = CatalogOfferMemory.Read(ctx.Facts);
        var recommendationMemory = CatalogRecommendationMemory.Read(ctx.Facts);
        if (memory is null && recommendationMemory is null)
            return [];

        var candidates = memory is null ? [] : CatalogOfferMemory.AllProducts(memory);
        var searchReference = NormalizeSelectionReference(productText);
        var requested = NormalizeTokens(searchReference);
        if (requested.Count == 0)
            return [];

        var requestedWords = requested.Where(token => !token.All(char.IsDigit)).ToList();
        var requestedNumbers = requested.Where(token => token.All(char.IsDigit)).ToList();

        var exact = candidates.Where(candidate =>
                Normalize(candidate.Name).Equals(Normalize(searchReference), StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(candidate.Sku)
                    && Normalize(candidate.Sku).Equals(Normalize(searchReference), StringComparison.Ordinal)))
            .ToList();
        var matches = exact;
        if (matches.Count == 0)
        {
            var textualMatches = candidates.Where(candidate =>
                TokensMatch(requestedWords, NormalizeTokens(candidate.Name))).ToList();
            var numericMatches = requestedNumbers.Count == 0
                ? []
                : textualMatches.Where(candidate =>
                    TokensMatch(requestedNumbers, NormalizeTokens(candidate.Name))).ToList();
            matches = numericMatches.Count > 0 ? numericMatches : textualMatches;
        }


        if (matches.Count == 0 && recommendationMemory is not null)
        {
            candidates = recommendationMemory.Products;
            matches = candidates.Where(candidate =>
                    Normalize(candidate.Name).Equals(Normalize(searchReference), StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(candidate.Sku)
                        && Normalize(candidate.Sku).Equals(Normalize(searchReference), StringComparison.Ordinal)))
                .ToList();
            if (matches.Count == 0)
            {
                var textualMatches = candidates.Where(candidate =>
                    TokensMatch(requestedWords, NormalizeTokens(candidate.Name))).ToList();
                var numericMatches = requestedNumbers.Count == 0
                    ? []
                    : textualMatches.Where(candidate =>
                        TokensMatch(requestedNumbers, NormalizeTokens(candidate.Name))).ToList();
                matches = numericMatches.Count > 0 ? numericMatches : textualMatches;
            }
        }

        return matches.Select(candidate => candidate.ToProductReference()).ToList();
    }

    private static IReadOnlyList<string> NormalizeTokens(string? value) =>
        NormalizeWords(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Length > 3 && token.EndsWith('s') ? token[..^1] : token)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool TokensMatch(
        IReadOnlyList<string> requested,
        IReadOnlyList<string> candidate) =>
        requested.All(token => candidate.Any(value =>
            value.Equals(token, StringComparison.Ordinal)
            || value.StartsWith(token, StringComparison.Ordinal)
            || token.StartsWith(value, StringComparison.Ordinal)));

    private static string Normalize(string? value) =>
        NormalizeWords(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizeSelectionReference(string? value)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "de", "del", "el", "la", "los", "las", "un", "una", "unos", "unas"
        };
        return string.Join(' ', NormalizeWords(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !ignored.Contains(token)));
    }

    private static string NormalizeWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var characters = decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters)
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
    public static IReadOnlyList<CartCommand> PreserveCatalogAmbiguity(
        AgentConversationContext context,
        string latestUserMessage,
        IReadOnlyList<CartCommand> commands)
    {
        var memory = CatalogOfferMemory.Read(context.Facts);
        if (memory is null || commands.Count == 0)
            return commands;

        var candidates = CatalogOfferMemory.AllProducts(memory);
        var latestOffer = memory.Snapshots.OrderByDescending(snapshot => snapshot.Sequence).First();
        static bool IsMeaningful(string token) =>
            token.Length >= 3 || token.Any(char.IsDigit);

        var messageTokens = NormalizeTokens(latestUserMessage)
            .Where(IsMeaningful).ToHashSet(StringComparer.Ordinal);
        var isOfferFollowUp = IsOfferFollowUp(context, latestOffer);
        var mentionedSelection = isOfferFollowUp
            ? FindUniqueMentionedProduct(context, latestOffer.Products, messageTokens)
            : null;
        var normalizedCommands = new List<CartCommand>();
        foreach (var command in commands)
        {
            var exactSelection = candidates.FirstOrDefault(candidate =>
                    Normalize(candidate.Name).Equals(Normalize(command.ProductText), StringComparison.Ordinal)
                    || !string.IsNullOrWhiteSpace(candidate.Sku)
                    && Normalize(candidate.Sku).Equals(Normalize(command.ProductText), StringComparison.Ordinal));
            var selected = exactSelection
                ?? (mentionedSelection is not null
                    && CanGroundMentionedSelection(
                        command.ProductText,
                        mentionedSelection)
                        ? mentionedSelection
                        : null);
            var normalizedCommand = command;
            if (selected is not null)
            {
                var selectedTokens = NormalizeTokens(selected.Name).Where(IsMeaningful).ToList();
                var mentionedTokens = selectedTokens
                    .Where(messageTokens.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (mentionedTokens.Count > 0)
                {
                    var compatible = candidates.Count(candidate =>
                    {
                        var candidateTokens = NormalizeTokens(candidate.Name).Where(IsMeaningful).ToList();
                        return mentionedTokens.All(token => candidateTokens.Contains(token, StringComparer.Ordinal));
                    });
                    normalizedCommand = compatible > 1
                        ? command with { ProductText = string.Join(' ', mentionedTokens) }
                        : command with { ProductText = selected.Name };
                }
            }

            if (selected is null
                && isOfferFollowUp
                && !string.IsNullOrWhiteSpace(latestOffer.ReplacementReference)
                && command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                && command.Quantity is > 0m)
            {
                normalizedCommands.Add(new CartCommand(
                    CartCommandOperations.Add,
                    BuildOfferRefinement(latestOffer, messageTokens),
                    command.Quantity,
                    command.DestinationReference));
                continue;
            }
            if (selected is not null
                && isOfferFollowUp
                && !string.IsNullOrWhiteSpace(latestOffer.ReplacementReference)
                && command.Operation is CartCommandOperations.Add or CartCommandOperations.SetQuantity
                && command.Quantity is > 0m)
            {
                var replaced = CartItemPresentationMemory.FindUniqueByReference(
                    context.Facts, latestOffer.ReplacementReference, context.Config?.Commerce.Matching);
                if (replaced is not null && !SameProduct(replaced, selected))
                {
                    normalizedCommands.Add(new CartCommand(
                        CartCommandOperations.Remove, replaced.ResolvedName, null, null));
                    normalizedCommands.Add(new CartCommand(
                        CartCommandOperations.Add, selected.Name, command.Quantity, command.DestinationReference));
                    continue;
                }
            }

            normalizedCommands.Add(normalizedCommand);
        }
        return normalizedCommands;
    }

    private static bool CanGroundMentionedSelection(
        string productText,
        ProductCandidate mentionedSelection)
    {
        var commandTokens = NormalizeTokens(productText)
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
        if (commandTokens.Count == 0)
            return true;

        var contextualTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "ese", "esa", "eso", "este", "esta", "aquel", "aquella",
            "mismo", "misma", "producto", "opcion"
        };
        if (commandTokens.All(contextualTokens.Contains))
            return true;

        var selectedTokens = NormalizeTokens(mentionedSelection.Name)
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
        return commandTokens.Any(selectedTokens.Contains);
    }

    private static string BuildOfferRefinement(
        CatalogOfferSnapshot offer,
        IReadOnlySet<string> messageTokens)
    {
        var candidateTokens = offer.Products
            .SelectMany(product => NormalizeTokens(product.Name))
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
        var refinement = messageTokens
            .Where(candidateTokens.Contains)
            .Where(token => !token.All(char.IsDigit))
            .ToList();
        if (refinement.Count > 0)
            return string.Join(' ', refinement);
        return offer.SearchTerms.FirstOrDefault(term => !string.IsNullOrWhiteSpace(term))
            ?? offer.ReplacementReference
            ?? string.Empty;
    }
    private static ProductCandidate? FindUniqueMentionedProduct(
        AgentConversationContext context,
        IReadOnlyList<ProductCandidate> candidates,
        IReadOnlySet<string> messageTokens)
    {
        var minimum = Math.Max(2, context.Config?.Commerce.Matching.ExactNameDominanceMinimumMatches ?? 0);
        var scored = candidates.Select(candidate => new
        {
            Candidate = candidate,
            Score = NormalizeTokens(candidate.Name)
                .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
                .Count(token => messageTokens.Contains(token))
        }).Where(value => value.Score >= minimum).ToList();
        if (scored.Count == 0)
            return null;
        var maximum = scored.Max(value => value.Score);
        var best = scored.Where(value => value.Score == maximum).ToList();
        return best.Count == 1 ? best[0].Candidate : null;
    }

    private static bool IsOfferFollowUp(
        AgentConversationContext context,
        CatalogOfferSnapshot offer)
    {
        var lastBotMessage = context.ConversationState?.LastBotMessage;
        if (string.IsNullOrWhiteSpace(lastBotMessage))
            return false;
        var botTokens = NormalizeTokens(lastBotMessage).ToHashSet(StringComparer.Ordinal);
        return offer.Products.Count(product => NormalizeTokens(product.Name)
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .All(botTokens.Contains)) >= Math.Min(2, offer.Products.Count);
    }

    private static bool SameProduct(
        CartItemPresentationEntry entry,
        ProductCandidate candidate) =>
        entry.ProductId.HasValue && candidate.ProductId == entry.ProductId
        || !string.IsNullOrWhiteSpace(entry.ExternalProductId)
            && candidate.ExternalProductId?.Equals(entry.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
        || !string.IsNullOrWhiteSpace(entry.Sku)
            && candidate.Sku?.Equals(entry.Sku, StringComparison.OrdinalIgnoreCase) == true
        || Normalize(entry.ResolvedName).Equals(Normalize(candidate.Name), StringComparison.Ordinal);

    public static bool TryGetSelected(AgentConversationContext ctx, out ProductCandidate candidate) =>
        TryReadCandidate(ctx, SelectedProductKey, out candidate);

    public static async Task ClearSelectedAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
        CancellationToken ct)
    {
        await factsService.ClearFieldsAsync(ctx.ConversationId, [SelectedProductKey], ct);
        ctx.Facts.Remove(SelectedProductKey);
    }

    private static async Task RememberSelectedAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
        ProductCandidate candidate,
        CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(candidate, JsonOptions);
        await factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, SelectedProductKey, value, rememberAcrossRequests: false, ct);
        ctx.Facts[SelectedProductKey] = value;
    }

    private static ProductCandidate? InferSingleCandidate(IReadOnlyList<ProductReference> products)
    {
        var activeProducts = products.Where(product => product.IsActive).ToList();
        return activeProducts.Count == 1 ? ProductCandidate.From(activeProducts[0]) : null;
    }

    private static bool TryReadCandidate(AgentConversationContext ctx, string key, out ProductCandidate candidate)
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
    decimal UnitPrice,
    string Currency = "COP",
    decimal? StockQuantity = null)
{
    public static ProductCandidate From(ProductReference product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            Truncate(product.Sku, 80),
            Truncate(product.Name, 140) ?? string.Empty,
            product.EffectiveUnitPrice ?? product.UnitPrice,
            product.Currency,
            product.StockQuantity);

    public ProductReference ToProductReference() =>
        new(
            ProductId,
            ExternalProductId,
            Sku,
            Name,
            null,
            null,
            UnitPrice,
            Currency,
            StockQuantity,
            UnitPrice);
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
