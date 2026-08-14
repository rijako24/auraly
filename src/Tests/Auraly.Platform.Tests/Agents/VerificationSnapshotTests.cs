using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Gating;
using ConversationStateModel = Auraly.Platform.Domain.Models.ConversationState;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class VerificationSnapshotTests
{
    [Fact]
    public void Matches_WhenAllDependencyFactsMatch_ReturnsTrue()
    {
        var snapshot = VerificationSnapshot.Serialize(new Dictionary<string, string>
        {
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-06-03",
            ["desired_time"] = "08:00"
        });

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.Service] = "Plan Marineritos",
            [ConversationFactKeys.DesiredDate] = "2026-06-03",
            [ConversationFactKeys.DesiredTime] = "08:00",
            [ConversationFactKeys.AddOns] = "Decoración Sencilla"
        };

        VerificationSnapshot.Matches(snapshot, current).Should().BeTrue();
    }

    [Fact]
    public void Matches_WhenDependencyFactChanged_ReturnsFalse()
    {
        var snapshot = VerificationSnapshot.Serialize(new Dictionary<string, string>
        {
            ["add_ons"] = "Decoración Sencilla"
        });

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.AddOns] = "Decoración Bouquet Personalizado"
        };

        VerificationSnapshot.Matches(snapshot, current).Should().BeFalse();
    }

    [Fact]
    public void Matches_EmptyPayload_IsAlwaysActive()
    {
        VerificationSnapshot.Matches(null, new Dictionary<string, string>()).Should().BeTrue();
    }

    [Fact]
    public void Of_UsesEmptyStringForMissingKeys()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.Service] = "Plan Marineritos"
        };

        var deps = VerificationSnapshot.Of(facts, ConversationFactKeys.Service, ConversationFactKeys.AddOns);

        deps[ConversationFactKeys.Service].Should().Be("Plan Marineritos");
        deps[ConversationFactKeys.AddOns].Should().BeEmpty();
    }
}

public sealed class ConversationVerificationServiceTests
{
    private readonly ConversationVerificationService _service = new();

    [Fact]
    public void RecordAndIsActive_MatchingFacts_ReturnsTrue()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-06-03";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";

        var deps = VerificationSnapshot.Of(ctx.Facts,
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime);

        _service.Record(ctx, VerificationFactTypes.AvailabilityChecked, deps, null);

        _service.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
            .Should().BeTrue();
    }

    [Fact]
    public void IsActive_WhenDependencyFactChanges_ReturnsFalse()
    {
        var ctx = CreateContext();
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";

        _service.Record(ctx, VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.Of(ctx.Facts, ConversationFactKeys.DesiredTime), null);

        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        _service.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActive_CustomerIdentifiedWithEmptySnapshot_ReturnsTrue()
    {
        var ctx = CreateContext();
        _service.Record(ctx, VerificationFactTypes.CustomerIdentified,
            new Dictionary<string, string>(), null);

        _service.IsActive(ctx.ConversationState, VerificationFactTypes.CustomerIdentified, ctx.Facts)
            .Should().BeTrue();
    }

    private static AgentConversationContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
