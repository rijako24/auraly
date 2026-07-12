using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Operations.Support;

internal static class PendingCartCommandMemory
{
    private const string FactKey = "system.pending_cart_commands";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<CartCommand> MergeResolution(
        AgentConversationContext context,
        IReadOnlyList<CartCommand> incoming)
    {
        var pending = Read(context);
        if (pending is null)
            return incoming;

        var pendingCommand = pending.Commands
            .First(command => SameReference(command.ProductText, pending.AmbiguousProductText));
        var latestMessage = context.LatestUserMessage ?? string.Empty;
        var latestCandidateMatches = pending.ProductCandidates
            .Where(candidate => IsReferenceMatch(latestMessage, candidate.Name))
            .ToList();
        var latestCatalogMatches = ProductSelectionMemory.FindCatalogMatches(context, latestMessage);
        var incomingResolution = incoming
            .Select(command => new
            {
                Command = command,
                CandidateMatches = pending.ProductCandidates
                    .Where(candidate => IsReferenceMatch(command.ProductText, candidate.Name))
                    .ToList(),
                CatalogMatches = ProductSelectionMemory.FindCatalogMatches(context, command.ProductText)
            })
            .FirstOrDefault(item => item.CandidateMatches.Count == 1 || item.CatalogMatches.Count == 1);

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
            return [];

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
        return merged;
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

    public static PendingCartCommandBatch? Read(AgentConversationContext context)
    {
        if (!context.Facts.TryGetValue(FactKey, out var raw) || string.IsNullOrWhiteSpace(raw))
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

internal sealed record PendingCartCommandBatch(
    int SchemaVersion,
    IReadOnlyList<CartCommand> Commands,
    string AmbiguousProductText,
    IReadOnlyList<CartCommandCandidate> ProductCandidates,
    DateTime ExpiresAtUtc);
