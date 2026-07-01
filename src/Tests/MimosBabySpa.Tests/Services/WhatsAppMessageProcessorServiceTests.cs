using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class WhatsAppMessageProcessorServiceTests
{
    [Fact]
    public async Task ProcessIncomingMessageAsync_WhenInboundContactSendsInteractiveMessage_DelegatesToInboundAgentWithMetadata()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        const string userNumber = "573023823535";
        const string messageText = "Aceptar";
        const string payload = "external_interaction:accepted:1559bd32-ec0b-4356-b98e-d2e754391c29";
        var inboundMetadata = new AgentInboundMetadata("wamid.inbound", "wamid.assignment", payload);

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            UserNumber = userNumber
        };

        var conversationService = new Mock<IConversationService>();
        conversationService
            .Setup(s => s.GetOrCreateConversationAsync(businessId, userNumber, "Supervoy"))
            .ReturnsAsync(conversation);

        var stateManager = new Mock<IConversationStateManager>();
        stateManager
            .Setup(s => s.GetStateByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationState { Owner = ConversationOwner.Bot });

        var router = new Mock<IBusinessInboundContactRouter>();
        router
            .Setup(r => r.ResolveAsync(businessId, userNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessInboundContactRoute(agentId, "supervoy", userNumber));

        var leadService = new Mock<ILeadService>(MockBehavior.Strict);
        var agent = new Mock<IAgentConversationService>();
        agent
            .Setup(a => a.ProcessMessageAsync(
                agentId,
                conversationId,
                messageText,
                userNumber,
                It.IsAny<CancellationToken>(),
                inboundMetadata))
            .ReturnsAsync(AgentTurnResult.Ok(string.Empty));

        var processor = new WhatsAppMessageProcessorService(
            conversationService.Object,
            stateManager.Object,
            Mock.Of<IMessageService>(),
            leadService.Object,
            Mock.Of<IWhatsAppService>(),
            agent.Object,
            Mock.Of<IAgentRepository>(),
            Mock.Of<IBlobStorageService>(),
            Mock.Of<IOutboundMessageDispatcher>(),
            router.Object,
            NullLogger<WhatsAppMessageProcessorService>.Instance);

        await processor.ProcessIncomingMessageAsync(
            businessId,
            userNumber,
            messageText,
            "Supervoy",
            inboundMetadata);

        agent.VerifyAll();
        leadService.VerifyNoOtherCalls();
    }
}
