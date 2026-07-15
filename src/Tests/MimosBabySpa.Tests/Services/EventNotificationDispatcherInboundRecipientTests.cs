using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class EventNotificationDispatcherInboundRecipientTests
{
    [Fact]
    public async Task SendEventAsync_InboundSelector_UsesConfiguredContactPhone()
    {
        var businessId = Guid.NewGuid();
        var contactPhone = "+573001112233";
        var contacts = new Mock<IBusinessInboundContactRepository>();
        contacts.Setup(value => value.GetActiveByBusinessAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BusinessInboundContact
                {
                    BusinessId = businessId,
                    Type = "payment_approver",
                    Key = "cj_payment_approver",
                    PhoneNumber = contactPhone,
                    IsActive = true
                }
            ]);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.BusinessInboundContacts).Returns(contacts.Object);
        var sequences = new Mock<IMessageSequenceResolver>();
        sequences.Setup(value => value.ResolveAsync(
                businessId,
                "approval_request",
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("Pago pendiente", null, "text")]);
        var outbound = new Mock<IOutboundMessageDispatcher>();
        var config = new AgentConfig
        {
            MessageSequences = new MessageSequenceCatalog
            {
                ["approval_request"] = new MessageSequence
                {
                    Messages = [new MessageSequenceStep { Body = "Pago pendiente" }]
                }
            },
            Notifications = new NotificationDefinitions
            {
                ["manual_payment_requested"] = new EventNotificationConfig
                {
                    Enabled = true,
                    Recipients = ["inbound:payment_approver"],
                    SendMessageSequence = "approval_request"
                }
            }
        };
        var dispatcher = new EventNotificationDispatcher(
            Mock.Of<IActiveAgentConfigResolver>(),
            sequences.Object,
            outbound.Object,
            unitOfWork.Object,
            NullLogger<EventNotificationDispatcher>.Instance);

        await dispatcher.SendEventAsync(
            businessId,
            config,
            "manual_payment_requested",
            new MessageSequenceContext(),
            CancellationToken.None);

        outbound.Verify(value => value.SendAllAsync(
            businessId,
            contactPhone,
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            null,
            It.IsAny<CancellationToken>(),
            false), Times.Once);
    }
}
