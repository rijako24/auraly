using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class SendMessageSequenceToolTests
{
    private readonly Mock<IMessageSequenceResolver> _resolver = new();
    private readonly SendMessageSequenceTool _tool;

    public SendMessageSequenceToolTests()
    {
        _tool = new SendMessageSequenceTool(_resolver.Object);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSequence_ReturnsError()
    {
        var ctx = CreateContext(catalog: new MessageSequenceCatalog());
        using var args = JsonDocument.Parse("""{"sequence":"missing"}""");

        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("unknown_sequence");
    }

    [Fact]
    public async Task ExecuteAsync_ValidSequence_EnqueuesOutboundMessages()
    {
        var catalog = new MessageSequenceCatalog
        {
            ["reservation_docs"] = new MessageSequence
            {
                Messages = [new MessageSequenceStep { Body = "Doc" }]
            }
        };

        _resolver.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                "reservation_docs",
                catalog,
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("Doc", null)]);

        var ctx = CreateContext(catalog);
        using var args = JsonDocument.Parse("""{"sequence":"reservation_docs"}""");

        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Turn!.OutboundMessages.Should().HaveCount(1);
        ctx.Turn.DirectOutboundRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SameSequenceTwice_SecondCallIsNoOp()
    {
        var catalog = new MessageSequenceCatalog
        {
            ["reservation_docs"] = new MessageSequence
            {
                Messages = [new MessageSequenceStep { Body = "Doc" }]
            }
        };

        _resolver.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                "reservation_docs",
                catalog,
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("Doc", null)]);

        var ctx = CreateContext(catalog);
        using var args = JsonDocument.Parse("""{"sequence":"reservation_docs"}""");

        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("already_queued");
        ctx.Turn!.OutboundMessages.Should().HaveCount(1);
    }


    [Fact]
    public void EnqueueOutbound_ByDefault_RemainsDeferred()
    {
        var ctx = CreateContext(new MessageSequenceCatalog());

        ctx.Turn!.EnqueueOutbound([new OutboundMessage("Doc", null)]);

        ctx.Turn.OutboundMessages.Should().HaveCount(1);
        ctx.Turn.DirectOutboundRequested.Should().BeFalse();
    }
    private static AgentToolContext CreateContext(MessageSequenceCatalog catalog)
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        return new AgentToolContext
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            BusinessToday = new DateOnly(2026, 6, 2),
            BusinessNow = DateTimeOffset.UtcNow,
            ChannelPhone = "+573001234567",
            ConversationState = new ConversationStateModel(),
            Conversation = new Conversation { ConversationId = Guid.NewGuid() },
            Config = new AgentConfig { MessageSequences = catalog },
            Turn = turn
        };
    }
}
