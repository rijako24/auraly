using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

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
            return new PendingCartMergeResult(incoming, true);

        var pendingCommand = pending.Commands
            .FirstOrDefault(command => SameReference(command.ProductText, pending.AmbiguousProductText));
        if (pendingCommand is null)
            return new PendingCartMergeResult(incoming, true);
        var latestMessage = context.LatestUserMessage ?? string.Empty;
        var latestCandidateMatches = pending.ProductCandidates
            .Where(candidate => IsReferenceMatch(latestMessage, candidate.Name))
            .ToList();
        var candidateBoundResolution = pending.ProductCandidates.Count > 0;
        var latestCatalogMatches = candidateBoundResolution
            ? []
            : ProductSelectionMemory.FindCatalogMatches(context, latestMessage)
                .Where(candidate => HasMeaningfulOverlap(pending.AmbiguousProductText, candidate.Name))
                .ToList();
        var incomingResolution = incoming
            .Select(command => new
            {
                Command = command,
                CandidateMatches = pending.ProductCandidates
                    .Where(candidate => IsReferenceMatch(command.ProductText, candidate.Name))
                    .ToList(),
                CatalogMatches = candidateBoundResolution
                    ? []
                    : ProductSelectionMemory.FindCatalogMatches(context, command.ProductText)
                        .Where(candidate => HasMeaningfulOverlap(pending.AmbiguousProductText, candidate.Name))
                        .ToList()
            })
            .FirstOrDefault(item => item.CandidateMatches.Count == 1 || item.CatalogMatches.Count == 1);

        var cancellation = incoming.FirstOrDefault(command =>
            command.Operation.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase)
            && SameReference(command.ProductText, pending.AmbiguousProductText));
        if (cancellation is not null)
        {
            var remaining = pending.Commands
                .Where(command => !SameReference(command.ProductText, pending.AmbiguousProductText))
                .Concat(incoming.Where(command => !ReferenceEquals(command, cancellation)))
                .ToList();
            return new PendingCartMergeResult(remaining, true);
        }

        var selectedName = latestCandidateMatches.Count == 1
            ? latestCandidateMatches[0].Name
            : latestCatalogMatches.Count == 1
                ? latestCatalogMatches[0].Name
                : incomingResolution?.CandidateMatches.Count == 1
                    ? incomingResolution.CandidateMatches[0].Name
                    : incomingResolution?.CatalogMatches.Count == 1
                        ? incomingResolution.CatalogMatches[0].Name
                        : null;
        if (selectedName is null)
            return new PendingCartMergeResult([], false);

        var replacement = pendingCommand with { ProductText = selectedName };
        var merged = pending.Commands
            .Select(command => SameReference(command.ProductText, pending.AmbiguousProductText)
                ? replacement
                : command)
            .ToList();
        var continuationCommand = incomingResolution?.Command
            ?? ((latestCandidateMatches.Count == 1 || latestCatalogMatches.Count == 1) && incoming.Count == 1
                ? incoming[0]
                : null);
        merged.AddRange(incoming.Where(command => !ReferenceEquals(command, continuationCommand)));
        return new PendingCartMergeResult(merged, true);
    }

    public static IReadOnlyList<CartCommand> AccumulateUnresolved(
        PendingCartCommandBatch pending,
        IReadOnlyList<CartCommand> incoming)
    {
        var accumulated = pending.Commands.ToList();
        foreach (var command in incoming)
        {
            if (SameReference(command.ProductText, pending.AmbiguousProductText))
                continue;

            var index = accumulated.FindIndex(existing =>
                SameReference(existing.ProductText, command.ProductText));
            if (index < 0)
            {
                accumulated.Add(command);
                continue;
            }

            var existing = accumulated[index];
            accumulated[index] = existing.Operation == CartCommandOperations.Add
                && command.Operation == CartCommandOperations.Add
                ? existing with { Quantity = (existing.Quantity ?? 0) + (command.Quantity ?? 0) }
                : command;
        }
        return accumulated;
    }
    public static async Task SaveAsync(
        IConversationFactsService facts,
        AgentConversationContext context,
        IReadOnlyList<CartCommand> commands,
        CartCommandIssue issue,
        CancellationToken cancellationToken)
    {
        var pending = new PendingCartCommandBatch(
            1,
            commands,
            issue.ProductText,
            issue.ProductCandidates.Count > 0
                ? issue.ProductCandidates
                : issue.Candidates.Select(name => new CartCommandCandidate(name, 0, string.Empty)).ToList(),
            DateTime.UtcNow.AddMinutes(30));
        var json = JsonSerializer.Serialize(pending, JsonOptions);
        await facts.SetAsync(
            context.ConversationId,
            context.BusinessId,
            FactKey,
            json,
            rememberAcrossRequests: false,
            cancellationToken);
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

    public static PendingCartCommandBatch? Read(AgentConversationContext context) =>
        Read(context.Facts);

    public static PendingCartCommandBatch? Read(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingCartCommandBatch>(raw, JsonOptions);
            return pending?.ExpiresAtUtc > DateTime.UtcNow ? pending : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsReferenceMatch(string reference, string candidate)
    {
        var tokens = NormalizeTokens(reference);
        var normalizedCandidate = Normalize(candidate);
        return tokens.Count > 0 && tokens.All(normalizedCandidate.Contains);
    }

    private static bool HasMeaningfulOverlap(string pendingReference, string candidateName)
    {
        static bool IsMeaningful(string token) =>
            token.Length >= 3 || token.Any(char.IsDigit);

        var pendingTokens = NormalizeTokens(pendingReference)
            .Where(IsMeaningful)
            .ToHashSet(StringComparer.Ordinal);
        return NormalizeTokens(candidateName)
            .Where(IsMeaningful)
            .Any(pendingTokens.Contains);
    }

    private static bool SameReference(string left, string right) =>
        Normalize(left).Equals(Normalize(right), StringComparison.Ordinal);

    private static IReadOnlyList<string> NormalizeTokens(string value) =>
        NormalizeWords(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Length > 3 && token.EndsWith('s') ? token[..^1] : token)
            .ToList();

    private static string Normalize(string value) =>
        NormalizeWords(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizeWords(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var characters = decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return new string(characters).Normalize(NormalizationForm.FormC);
    }
}

internal sealed record PendingCartMergeResult(
    IReadOnlyList<CartCommand> Commands,
    bool Resolved);

internal sealed record PendingCartCommandBatch(
    int SchemaVersion,
    IReadOnlyList<CartCommand> Commands,
    string AmbiguousProductText,
    IReadOnlyList<CartCommandCandidate> ProductCandidates,
    DateTime ExpiresAtUtc);
