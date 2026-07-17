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
        var inboundMetadata = new AgentInboundMetadata(
            "wamid.inbound", "wamid.assignment", payload, RecipientPhoneNumberId: "phone-id");

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

        var typingSession = new Mock<IAsyncDisposable>();
        typingSession.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var whatsApp = new Mock<IWhatsAppService>();
        whatsApp
            .Setup(s => s.StartTypingIndicatorAsync(
                businessId, "phone-id", "wamid.inbound", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typingSession.Object);

        var processor = new WhatsAppMessageProcessorService(
            conversationService.Object,
            stateManager.Object,
            Mock.Of<IMessageService>(),
            leadService.Object,
            whatsApp.Object,
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
        whatsApp.VerifyAll();
        typingSession.Verify(s => s.DisposeAsync(), Times.Once);
        leadService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessIncomingMessageAsync_WhenAgentTurnFails_ThrowsSoInboundCanRetry()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        const string userNumber = "573013161564";
        const string messageText = "Corte premium de adulto";

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            UserNumber = userNumber
        };

        var conversationService = new Mock<IConversationService>();
        conversationService
            .Setup(s => s.GetOrCreateConversationAsync(businessId, userNumber, "Jorge Torres"))
            .ReturnsAsync(conversation);

        var stateManager = new Mock<IConversationStateManager>();
        stateManager
            .Setup(s => s.GetStateByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationState { Owner = ConversationOwner.Bot });

        var leadService = new Mock<ILeadService>();
        leadService
            .Setup(s => s.GetOrCreateLeadAsync(businessId, userNumber, "Jorge Torres"))
            .ReturnsAsync(new Lead { LeadId = Guid.NewGuid(), BusinessId = businessId, UserNumber = userNumber });

        var agentRepository = new Mock<IAgentRepository>();
        agentRepository
            .Setup(r => r.GetActiveCustomerByBusinessAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { AgentId = agentId, BusinessId = businessId, Name = "Luis", IsActive = true });

        var agent = new Mock<IAgentConversationService>();
        agent
            .Setup(a => a.ProcessMessageAsync(
                agentId,
                conversationId,
                messageText,
                userNumber,
                It.IsAny<CancellationToken>(),
                null))
            .ReturnsAsync(AgentTurnResult.Fail("llm timeout"));

        var processor = new WhatsAppMessageProcessorService(
            conversationService.Object,
            stateManager.Object,
            Mock.Of<IMessageService>(),
            leadService.Object,
            Mock.Of<IWhatsAppService>(),
            agent.Object,
            agentRepository.Object,
            Mock.Of<IBlobStorageService>(),
            Mock.Of<IOutboundMessageDispatcher>(),
            Mock.Of<IBusinessInboundContactRouter>(),
            NullLogger<WhatsAppMessageProcessorService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessIncomingMessageAsync(
            businessId,
            userNumber,
            messageText,
            "Jorge Torres"));

        Assert.Contains("AgentConversationService fallo", ex.Message);
    }
}