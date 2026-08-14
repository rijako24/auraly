using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Runtime;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Models;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class DeterministicTurnEffectProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ResolvesNotificationAndSequenceBeforeCompletingRequest()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte",
            ["customer_name"] = "Richard"
        };
        var config = new AgentConfig
        {
            BusinessId = businessId,
            MessageSequences = new MessageSequenceCatalog
            {
                ["reservation_confirmation"] = new MessageSequence()
            },
            Notifications = new NotificationDefinitions
            {
                ["reservation_created"] = new EventNotificationConfig { Enabled = true }
            }
        };
        var notification = new Mock<IEventNotificationDispatcher>();
        var sequences = new Mock<IMessageSequenceResolver>();
        var requestContext = new Mock<IRequestContextService>();
        var order = new MockSequence();
        MessageSequenceContext? notificationContext = null;
        MessageSequenceContext? sequenceContext = null;
        notification.InSequence(order).Setup(value => value.SendEventAsync(
                businessId,
                config,
                "reservation_created",
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, AgentConfig, string, MessageSequenceContext, CancellationToken>((_, _, _, context, _) =>
                notificationContext = context)
            .Returns(Task.CompletedTask);
        sequences.InSequence(order).Setup(value => value.ResolveAsync(
                businessId,
                "reservation_confirmation",
                config.MessageSequences,
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MessageSequenceCatalog, MessageSequenceContext, CancellationToken>((_, _, _, context, _) =>
                sequenceContext = context)
            .ReturnsAsync([new OutboundMessage("Reserva creada", null)]);
        requestContext.InSequence(order).Setup(value => value.CompleteAsync(
                conversationId,
                config,
                It.IsAny<ConversationState>(),
                facts,
                "request_completed",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContextCleanupResult("request_completed", [], []));
        var processor = new DeterministicTurnEffectProcessor(
            [new ReservationCreatedOperationEventContextResolver()],
            notification.Object,
            sequences.Object,
            requestContext.Object);
        var result = new DeterministicTurnResult
        {
            Success = true,
            Events = ["reservation_created"],
            DomainEvents =
            [
                OperationEvent.Create("reservation_created", new
                {
                    reservationId,
                    service = "Corte",
                    date = "2026-07-12",
                    time = "09:00",
                    customerName = "Richard",
                    customerPhone = "+573001112233"
                })
            ],
            Sequences = ["reservation_confirmation"],
            RequestCompleted = true
        };

        var effectResult = await processor.ProcessAsync(
            new DeterministicTurnEffectRequest(
                businessId,
                conversationId,
                config,
                new ConversationState(),
                facts,
                result),
            CancellationToken.None);

        effectResult.OutboundMessages.Should().ContainSingle().Which.Body.Should().Be("Reserva creada");
        effectResult.DispatchedNotificationEvents.Should().Equal("reservation_created");
        effectResult.RequestContextCompleted.Should().BeTrue();
        notificationContext.Should().NotBeNull();
        notificationContext!.Reservation!.ReservationId.Should().Be(reservationId);
        notificationContext.Custom["service"].Should().Be("Corte");
        notificationContext.Custom["source_conversation_id"].Should().Be(conversationId.ToString());
        sequenceContext!.Reservation!.ReservationId.Should().Be(reservationId);
        notification.Verify(value => value.SendEventAsync(
            businessId,
            config,
            "reservation_created",
            It.IsAny<MessageSequenceContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ForFailedTurn_ExecutesNoExternalEffects()
    {
        var notification = new Mock<IEventNotificationDispatcher>();
        var sequences = new Mock<IMessageSequenceResolver>();
        var requestContext = new Mock<IRequestContextService>();
        var processor = new DeterministicTurnEffectProcessor(
            [new ReservationCreatedOperationEventContextResolver()],
            notification.Object,
            sequences.Object,
            requestContext.Object);

        var result = await processor.ProcessAsync(
            new DeterministicTurnEffectRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new AgentConfig(),
                new ConversationState(),
                new Dictionary<string, string>(),
                new DeterministicTurnResult
                {
                    Success = false,
                    Events = ["reservation_created"],
                    RequestCompleted = true
                }),
            CancellationToken.None);

        result.OutboundMessages.Should().BeEmpty();
        result.RequestContextCompleted.Should().BeFalse();
        notification.VerifyNoOtherCalls();
        sequences.VerifyNoOtherCalls();
        requestContext.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReservationContextResolver_WithMalformedSchedule_PreservesPayloadWithoutThrowing()
    {
        var reservationId = Guid.NewGuid();
        var resolver = new ReservationCreatedOperationEventContextResolver();

        var context = await resolver.ResolveAsync(
            OperationEvent.Create("reservation_created", new
            {
                reservationId,
                date = "not-a-date",
                time = "not-a-time",
                customerName = "Ana"
            }),
            new Dictionary<string, string>(),
            CancellationToken.None);

        context.Reservation!.ReservationId.Should().Be(reservationId);
        context.Reservation.ReservationDateTime.Should().BeNull();
        context.Custom["customerName"].Should().Be("Ana");
    }
}
