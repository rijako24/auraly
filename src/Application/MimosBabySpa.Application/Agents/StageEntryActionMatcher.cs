using System.Globalization;
using System.Text;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using ConversationState = MimosBabySpa.Domain.Models.ConversationState;

namespace MimosBabySpa.Application.Agents;

internal static class StageEntryActionMatcher
{
    public static bool Matches(StageEntryAction action, AgentToolContext ctx) =>
        Matches(action.When, ctx.Facts, ctx.LatestUserMessage, ctx.ConversationState);

    public static bool Matches(
        StageEntryActionCondition condition,
        IReadOnlyDictionary<string, string> facts,
        string? latestUserMessage,
        ConversationState? state = null)
    {
        foreach (var factKey in condition.RequiredFacts)
        {
            if (IsMissingFact(facts, factKey))
                return false;
        }

        foreach (var factKey in condition.MissingFacts)
        {
            if (!IsMissingFact(facts, factKey))
                return false;
        }

        foreach (var verificationType in condition.MissingVerifications)
        {
            if (!IsMissingVerification(state, verificationType, facts))
                return false;
        }

        if (condition.MessageMatches.Count == 0)
            return true;

        var normalizedMessage = NormalizeIntentText(latestUserMessage);
        return condition.MessageMatches.Any(match => MatchesEntryActionMessage(match, normalizedMessage));
    }

    private static bool MatchesEntryActionMessage(StageEntryMessageMatch match, string normalizedMessage)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage))
            return false;

        return match.AnyOf.Any(candidate =>
        {
            var normalizedCandidate = NormalizeIntentText(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                return false;

            return ContainsNormalizedPhrase(normalizedMessage, normalizedCandidate);
        });
    }

    private static bool ContainsNormalizedPhrase(string normalizedMessage, string normalizedCandidate) =>
        $" {normalizedMessage} ".Contains($" {normalizedCandidate} ", StringComparison.Ordinal);

    private static bool IsMissingFact(IReadOnlyDictionary<string, string> facts, string factKey) =>
        !facts.TryGetValue(factKey, out var value) || string.IsNullOrWhiteSpace(value);

    private static bool IsMissingVerification(
        ConversationState? state,
        string verificationType,
        IReadOnlyDictionary<string, string> facts)
    {
        if (state is null || string.IsNullOrWhiteSpace(verificationType))
            return true;

        if (!state.Verifications.TryGetValue(verificationType, out var entry))
            return true;

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return true;

        return !VerificationSnapshot.Matches(entry.PayloadJson, facts);
    }

    private static string NormalizeIntentText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}