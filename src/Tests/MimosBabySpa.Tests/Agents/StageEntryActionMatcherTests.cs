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
    public void Matches_WithOptionalTimeArgument_ReturnsTrueUntilAvailabilityIsChecked()
    {
        var condition = new StageEntryActionCondition
        {
            RequiredFacts = ["service", "desired_date"],
            MissingFacts = ["availability_checked"]
        };
        var factsWithoutTime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-07-10"
        };
        var factsWithTime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-07-10",
            ["desired_time"] = "10:00"
        };
        var factsAlreadyChecked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-07-10",
            ["desired_time"] = "10:00",
            ["availability_checked"] = "true"
        };

        StageEntryActionMatcher.Matches(condition, factsWithoutTime, latestUserMessage: null)
            .Should().BeTrue();
        StageEntryActionMatcher.Matches(condition, factsWithTime, latestUserMessage: null)
            .Should().BeTrue();
        StageEntryActionMatcher.Matches(condition, factsAlreadyChecked, latestUserMessage: null)
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