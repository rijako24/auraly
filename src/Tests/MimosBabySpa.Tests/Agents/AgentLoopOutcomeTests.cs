using FluentAssertions;
using MimosBabySpa.Application.Agents;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentLoopOutcomeTests
{
    [Fact]
    public void Completed_WithEmptyResponse_RepresentsDirectUserDeliveryTurn()
    {
        var outcome = AgentLoopOutcome.Completed(string.Empty);

        outcome.Kind.Should().Be(AgentLoopOutcome.OutcomeKind.Completed);
        outcome.Response.Should().BeEmpty();
    }
}
