using FluentAssertions;
using MimosBabySpa.Application.Agents;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentTurnExecutionOutboundTests
{
    [Fact]
    public void EnqueueOutbound_AddsMessagesToTurnResult()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var messages = new[]
        {
            new OutboundMessage("Hola", null),
            new OutboundMessage("Adjunto", "https://example.com/doc.pdf", "document", "doc.pdf")
        };

        turn.EnqueueOutbound(messages);

        turn.OutboundMessages.Should().HaveCount(2);
        var result = turn.ToSuccessResult("confirmación");
        result.Response.Should().Be("confirmación");
        result.OutboundMessages.Should().HaveCount(2);
    }

    [Fact]
    public void OutboundMessages_SignalDirectUserDeliveryTurnCompletesWithoutLlmText()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        turn.EnqueueOutbound([new OutboundMessage("Confirmado", null)]);

        turn.OutboundMessages.Count.Should().BeGreaterThan(0);

        var loopOutcome = AgentLoopOutcome.Completed(string.Empty);
        loopOutcome.Kind.Should().Be(AgentLoopOutcome.OutcomeKind.Completed);
        loopOutcome.Response.Should().BeEmpty();
    }

    [Fact]
    public void TryMarkSequenceEnqueued_PreventsDuplicateSequenceInSameTurn()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);

        turn.TryMarkSequenceEnqueued("reservation_docs").Should().BeTrue();
        turn.TryMarkSequenceEnqueued("reservation_docs").Should().BeFalse();
        turn.TryMarkSequenceEnqueued("other").Should().BeTrue();
    }
}
