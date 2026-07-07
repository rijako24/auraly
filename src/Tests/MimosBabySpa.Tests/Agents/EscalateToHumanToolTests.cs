using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class EscalateToHumanToolTests
{
    [Fact]
    public async Task ExecuteAsync_NotifiesHumanContactsWithoutDisablingBot()
    {
        var notifier = new RecordingEscalationNotifier();
        var unitOfWork = CreateUnitOfWork();
        var tool = new EscalateToHumanTool(notifier);
        var state = new ConversationState { Owner = ConversationOwner.Bot };
        var ctx = new AgentToolContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ChannelPhone = "573001112233",
            ConversationState = state,
            Conversation = new Conversation(),
            EscalationContacts = ["573009998888"],
            Facts = []
        };

        using var args = JsonDocument.Parse(
            """{"reason":"explicit_human_request","last_user_message":"Necesito hablar con alguien"}""");

        var raw = await tool.ExecuteAsync(args.RootElement, ctx);

        state.Owner.Should().Be(ConversationOwner.Bot);
        state.LastEscalatedAt.Should().NotBeNull();
        notifier.Notifications.Should().ContainSingle();
        raw.Should().Contain("bot remains active");
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    private static Mock<IUnitOfWork> CreateUnitOfWork()
    {
        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.UpdateAsync(It.IsAny<Reservation>()))
            .ReturnsAsync((Reservation reservation) => reservation);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservations.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private sealed class RecordingEscalationNotifier : IEscalationNotifier
    {
        public List<EscalationNotification> Notifications { get; } = [];

        public Task<bool> NotifyAsync(
            Guid businessId,
            IReadOnlyList<string> contacts,
            EscalationNotification notification,
            CancellationToken ct = default)
        {
            Notifications.Add(notification);
            return Task.FromResult(true);
        }
    }
}
