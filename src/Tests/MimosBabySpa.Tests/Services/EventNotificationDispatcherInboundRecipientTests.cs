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
    [Fact]
    public async Task SendEventAsync_UnresolvedInternalSelector_DoesNotFallbackToCustomer()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var contacts = new Mock<IBusinessInboundContactRepository>();
        contacts.Setup(value => value.GetActiveByBusinessAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var conversations = new Mock<IConversationRepository>();
        conversations.Setup(value => value.GetByIdAsync(conversationId))
            .ReturnsAsync(new Conversation
            {
                ConversationId = conversationId,
                BusinessId = businessId,
                UserNumber = "+573009998877"
            });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.BusinessInboundContacts).Returns(contacts.Object);
        unitOfWork.SetupGet(value => value.Conversations).Returns(conversations.Object);
        var sequences = new Mock<IMessageSequenceResolver>();
        var outbound = new Mock<IOutboundMessageDispatcher>();
        var config = new AgentConfig
        {
            MessageSequences = new MessageSequenceCatalog
            {
                ["internal_order"] = new MessageSequence
                {
                    Messages = [new MessageSequenceStep { Body = "Pedido interno" }]
                }
            },
            Notifications = new NotificationDefinitions
            {
                ["order_created"] = new EventNotificationConfig
                {
                    Enabled = true,
                    Deliveries =
                    [
                        new EventNotificationDeliveryConfig
                        {
                            Id = "internal",
                            Recipients = ["inbound:payment_approver"],
                            SendMessageSequence = "internal_order"
                        }
                    ]
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
            "order_created",
            new MessageSequenceContext
            {
                Custom = new Dictionary<string, string>
                {
                    ["source_conversation_id"] = conversationId.ToString()
                }
            },
            CancellationToken.None);

        outbound.Verify(value => value.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
        conversations.Verify(value => value.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }
    [Fact]
    public async Task SendEventAsync_MultipleDeliveries_SendCustomerAndInternalMessages()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var customerPhone = "+573001110000";
        var internalPhone = "+573002220000";

        var contacts = new Mock<IBusinessInboundContactRepository>();
        contacts.Setup(value => value.GetActiveByBusinessAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BusinessInboundContact
            {
                BusinessId = businessId,
                Type = "payment_approver",
                Key = "orders",
                PhoneNumber = internalPhone,
                IsActive = true
            }]);
        var conversations = new Mock<IConversationRepository>();
        conversations.Setup(value => value.GetByIdAsync(conversationId))
            .ReturnsAsync(new Conversation
            {
                ConversationId = conversationId,
                BusinessId = businessId,
                UserNumber = customerPhone
            });
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.BusinessInboundContacts).Returns(contacts.Object);
        unitOfWork.SetupGet(value => value.Conversations).Returns(conversations.Object);

        var sequences = new Mock<IMessageSequenceResolver>();
        sequences.Setup(value => value.ResolveAsync(
                businessId,
                It.IsAny<string>(),
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string name, MessageSequenceCatalog _, MessageSequenceContext _, CancellationToken _) =>
                [new OutboundMessage(name, null, "text")]);
        var outbound = new Mock<IOutboundMessageDispatcher>();
        var config = new AgentConfig
        {
            MessageSequences = new MessageSequenceCatalog
            {
                ["customer_order"] = new MessageSequence { Messages = [new MessageSequenceStep { Body = "Cliente" }] },
                ["internal_order"] = new MessageSequence { Messages = [new MessageSequenceStep { Body = "Interno" }] }
            },
            Notifications = new NotificationDefinitions
            {
                ["order_created"] = new EventNotificationConfig
                {
                    Enabled = true,
                    Deliveries =
                    [
                        new EventNotificationDeliveryConfig
                        {
                            Id = "customer",
                            Recipients = ["source:conversation"],
                            SendMessageSequence = "customer_order"
                        },
                        new EventNotificationDeliveryConfig
                        {
                            Id = "internal",
                            Recipients = ["inbound:payment_approver"],
                            SendMessageSequence = "internal_order"
                        }
                    ]
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
            "order_created",
            new MessageSequenceContext
            {
                Custom = new Dictionary<string, string>
                {
                    ["source_conversation_id"] = conversationId.ToString()
                }
            },
            CancellationToken.None);

        outbound.Verify(value => value.SendAllAsync(
            businessId, customerPhone, It.IsAny<IReadOnlyList<OutboundMessage>>(), conversationId,
            It.IsAny<CancellationToken>(), false), Times.Once);
        outbound.Verify(value => value.SendAllAsync(
            businessId, internalPhone, It.IsAny<IReadOnlyList<OutboundMessage>>(), null,
            It.IsAny<CancellationToken>(), false), Times.Once);
    }
}
