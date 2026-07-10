using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class StageEntryActionMatcherTests
{
    [Fact]
    public void Matches_WithRequiredFacts_ReturnsFalseWhenAnyRequiredFactIsMissing()
    {
        var condition = new StageEntryActionCondition
        {
            RequiredFacts = ["service", "desired_date"]
        };
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte basico"
        };

        StageEntryActionMatcher.Matches(condition, facts, latestUserMessage: null)
            .Should().BeFalse();
    }

    [Fact]
    public void Matches_WithMissingVerification_ReturnsTrueWhenVerificationIsStale()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte premium"
        };
        var state = new ConversationState();
        state.Verifications[VerificationFactTypes.CheckoutPrepared] = new VerificationEntry(
            DateTime.UtcNow,
            ExpiresAt: null,
            VerificationSnapshot.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Corte basico"
            }));

        var condition = new StageEntryActionCondition
        {
            RequiredFacts = ["service"],
            MissingVerifications = [VerificationFactTypes.CheckoutPrepared]
        };

        StageEntryActionMatcher.Matches(condition, facts, latestUserMessage: null, state)
            .Should().BeTrue();
    }

    [Fact]
    public void Matches_WithMissingVerification_ReturnsFalseWhenVerificationIsCurrent()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte basico"
        };
        var state = new ConversationState();
        state.Verifications[VerificationFactTypes.CheckoutPrepared] = new VerificationEntry(
            DateTime.UtcNow,
            ExpiresAt: null,
            VerificationSnapshot.Serialize(facts));

        var condition = new StageEntryActionCondition
        {
            RequiredFacts = ["service"],
            MissingVerifications = [VerificationFactTypes.CheckoutPrepared]
        };

        StageEntryActionMatcher.Matches(condition, facts, latestUserMessage: null, state)
            .Should().BeFalse();
    }
}