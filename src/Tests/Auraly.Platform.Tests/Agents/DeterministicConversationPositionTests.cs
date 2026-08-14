using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Runtime;
using Auraly.Platform.Domain.Models;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class DeterministicConversationPositionTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExpireSecondaryFlowIfNeeded_ReturnsToPrimaryAfterConfiguredInactivity()
    {
        var state = new ConversationState
        {
            ActiveFlowId = "reservation_management",
            ActiveStageId = "change",
            ActiveFlowExpiresAtUtc = Now.AddSeconds(-1)
        };

        var expired = DeterministicConversationPosition.ExpireSecondaryFlowIfNeeded(Config(), state, Now);

        expired.Should().BeTrue();
        state.ActiveFlowId.Should().Be("booking");
        state.ActiveStageId.Should().BeNull();
        state.ActiveFlowExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void ExpireSecondaryFlowIfNeeded_PreservesUnexpiredSecondaryFlow()
    {
        var expires = Now.AddMinutes(4);
        var state = new ConversationState
        {
            ActiveFlowId = "reservation_management",
            ActiveStageId = "change",
            ActiveFlowExpiresAtUtc = expires
        };

        var expired = DeterministicConversationPosition.ExpireSecondaryFlowIfNeeded(Config(), state, Now);

        expired.Should().BeFalse();
        state.ActiveFlowId.Should().Be("reservation_management");
        state.ActiveStageId.Should().Be("change");
        state.ActiveFlowExpiresAtUtc.Should().Be(expires);
    }

    [Fact]
    public void RefreshFlowLease_UsesConfiguredTtlAndClearsItForPrimary()
    {
        var config = Config();
        var state = new ConversationState { ActiveFlowId = "reservation_management" };

        DeterministicConversationPosition.RefreshFlowLease(config, state, Now);
        state.ActiveFlowExpiresAtUtc.Should().Be(Now.AddSeconds(600));

        state.ActiveFlowId = "booking";
        DeterministicConversationPosition.RefreshFlowLease(config, state, Now);
        state.ActiveFlowExpiresAtUtc.Should().BeNull();
    }

    private static AgentConfig Config() => new()
    {
        Flows =
        [
            new AgentFlowDefinition { Id = "booking", Type = FlowTypes.Primary },
            new AgentFlowDefinition
            {
                Id = "reservation_management",
                Type = FlowTypes.Secondary,
                TtlSeconds = 600
            }
        ]
    };
}