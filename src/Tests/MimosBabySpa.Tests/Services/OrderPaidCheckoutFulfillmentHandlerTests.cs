using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class PaymentConfirmationHandlerTests
{
    [Fact]
    public async Task SendCustomerSequenceAsync_UsesConversationNumberForCustomerReply()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var payment = new PaymentTransaction
        {
            BusinessId = businessId,
            ConversationId = conversationId,
            PaymentReferenceId = "test_IDwxrC",
            CheckoutKind = CheckoutKind.Order
        };

        var config = new AgentConfig
        {
            BusinessId = businessId,
            Webhooks = new WebhookDefinitions
            {
                Wompi = new Dictionary<string, WompiWebhookOutcomeConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    ["order_paid"] = new() { SendMessageSequence = "order_paid_customer" }
                }
            },
            MessageSequences = new MessageSequenceCatalog()
        };

        var result = new PaidCheckoutFulfillmentResult(
            "order_paid",
            "order_created",
            Guid.NewGuid(),
            CustomerPhone: "301292660",
            CustomPayload: new Dictionary<string, string>(),
            CompletionReason: "payment_order_confirmed");

        var conversations = new Mock<IConversationRepository>();
        conversations.Setup(r => r.GetByIdAsync(conversationId))
            .ReturnsAsync(new Conversation
            {
                BusinessId = businessId,
                ConversationId = conversationId,
                UserNumber = "573012926660"
            });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Conversations).Returns(conversations.Object);

        var resolver = new Mock<IMessageSequenceResolver>();
        resolver.Setup(r => r.ResolveAsync(
                businessId,
                "order_paid_customer",
                config.MessageSequences,
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("Pago confirmado", null)]);

        var dispatcher = new Mock<IOutboundMessageDispatcher>();
        var handler = new PaymentConfirmationHandler(
            unitOfWork.Object,
            Mock.Of<IConversationStateManager>(),
            Mock.Of<IPaymentLifecycleService>(),
            Mock.Of<IActiveAgentConfigResolver>(),
            Mock.Of<IRequestContextService>(),
            resolver.Object,
            dispatcher.Object,
            Mock.Of<IEventNotificationDispatcher>(),
            Mock.Of<IExternalEscalationService>(),
            Mock.Of<IPaidCheckoutFulfillmentRegistry>(),
            NullLogger<PaymentConfirmationHandler>.Instance);

        var method = typeof(PaymentConfirmationHandler).GetMethod(
            "SendCustomerSequenceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var task = (Task)method!.Invoke(handler, [payment, config, result, CancellationToken.None])!;
        await task;

        dispatcher.Verify(d => d.SendAllAsync(
            businessId,
            "573012926660",
            It.Is<IReadOnlyList<OutboundMessage>>(messages => messages.Count == 1),
            conversationId,
            It.IsAny<CancellationToken>(),
            false), Times.Once);
    }
}
