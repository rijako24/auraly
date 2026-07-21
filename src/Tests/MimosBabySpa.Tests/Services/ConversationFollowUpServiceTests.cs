using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class ConversationFollowUpServiceTests
{
    [Fact]
    public async Task ScheduleAfterDeliveredTurnAsync_StoresOneWaitOnConversationState()
    {
        var fixture = CreateFixture(due: false);
        var waitingSince = fixture.Source.Timestamp.AddSeconds(-1);

        await fixture.Service.ScheduleAfterDeliveredTurnAsync(
            fixture.Config.AgentId,
            fixture.Conversation,
            waitingSince);

        fixture.State.PendingCustomerReply.Should().NotBeNull();
        fixture.State.PendingCustomerReply!.SourceMessageId.Should().Be(fixture.Source.MessageId);
        fixture.State.PendingCustomerReply.RequestGeneration.Should().Be(fixture.State.RequestGeneration);
        fixture.State.FollowUpDueAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(119));
        fixture.StateManager.Verify(manager => manager.SaveStateAsync(
            fixture.Conversation.ConversationId,
            fixture.State,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelPendingAsync_ClearsWaitSoNextTurnCanCreateAnother()
    {
        var fixture = CreateFixture(due: true);

        await fixture.Service.CancelPendingAsync(fixture.Conversation.ConversationId);

        fixture.State.PendingCustomerReply.Should().BeNull();
        fixture.State.FollowUpDueAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenWaitIsDueAndStillCurrent_SendsOnlyOneContextualFollowUp()
    {
        var fixture = CreateFixture(due: true);

        await fixture.Service.RunAsync();
        await fixture.Service.RunAsync();

        fixture.Renderer.Verify(renderer => renderer.RenderFollowUpAsync(
            It.IsAny<DeterministicFollowUpRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Dispatcher.Verify(dispatcher => dispatcher.SendAllAsync(
            fixture.Conversation.BusinessId,
            fixture.Conversation.UserNumber,
            It.Is<IReadOnlyList<OutboundMessage>>(messages =>
                messages.Count == 1 && messages[0].Body == "¿Quieres que continuemos con esa opción?"),
            fixture.Conversation.ConversationId,
            It.IsAny<CancellationToken>(),
            true), Times.Once);
        fixture.State.PendingCustomerReply!.FollowUpSentAtUtc.Should().NotBeNull();
        fixture.State.FollowUpDueAtUtc.Should().BeNull();
    }

    private static Fixture CreateFixture(bool due)
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var source = new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            Sender = "bot",
            MessageText = "¿Cuál opción prefieres?",
            Timestamp = DateTime.UtcNow.AddMinutes(-5)
        };
        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            UserNumber = "573001234567",
            Status = ConversationLifecycleStatus.Active
        };
        var state = new ConversationState
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            Owner = ConversationOwner.Bot,
            ActiveFlowId = "sales",
            ActiveStageId = "choose",
            RequestGeneration = 4,
            Version = 1
        };
        if (due)
        {
            state.CustomerReplyExpectationVersion = 1;
            state.FollowUpDueAtUtc = DateTime.UtcNow.AddMinutes(-1);
            state.PendingCustomerReply = new PendingCustomerReply
            {
                Version = 1,
                AgentId = agentId,
                RequestGeneration = 4,
                FlowId = "sales",
                StageId = "choose",
                SourceMessageId = source.MessageId,
                WaitingSinceUtc = source.Timestamp.AddSeconds(-1)
            };
        }

        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = businessId,
            ConversationFollowUp = new ConversationFollowUpDefinitions
            {
                Enabled = true,
                DelayMinutes = 120,
                Guidance = "Retoma la pregunta pendiente."
            },
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "sales",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "choose",
                            Goal = "El cliente elige una opción."
                        }
                    ]
                }
            ]
        };

        var stateRepository = new Mock<IConversationStateRepository>();
        stateRepository.Setup(repository => repository.GetDueFollowUpConversationIdsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([conversationId]);
        var stateManager = new Mock<IConversationStateManager>();
        stateManager.Setup(manager => manager.GetStateByConversationIdAsync(
                conversationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        stateManager.Setup(manager => manager.SaveStateAsync(
                conversationId,
                It.IsAny<ConversationState>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ConversationState saved, CancellationToken _) => saved);

        var conversations = new Mock<IConversationService>();
        conversations.Setup(service => service.GetConversationByIdAsync(conversationId))
            .ReturnsAsync(conversation);
        var messages = new Mock<IMessageService>();
        messages.Setup(service => service.GetRecentConversationHistoryAsync(
                conversationId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([source]);
        var inbound = new Mock<IInboundMessageDeduplicationService>();
        inbound.Setup(service => service.HasConversationMessageReceivedAfterAsync(
                businessId,
                "whatsapp",
                conversation.UserNumber,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var configs = new Mock<IAgentConfigProvider>();
        configs.Setup(provider => provider.GetConfigAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        var facts = new Mock<IConversationFactsService>();
        facts.Setup(service => service.GetAllAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        var renderer = new Mock<IDeterministicResponseRenderer>();
        renderer.Setup(service => service.RenderFollowUpAsync(
                It.IsAny<DeterministicFollowUpRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicRenderedResponse(
                "¿Quieres que continuemos con esa opción?",
                12,
                8));
        var dispatcher = new Mock<IOutboundMessageDispatcher>();
        dispatcher.Setup(service => service.SendAllAsync(
                businessId,
                conversation.UserNumber,
                It.IsAny<IReadOnlyList<OutboundMessage>>(),
                conversationId,
                It.IsAny<CancellationToken>(),
                true))
            .Returns(Task.CompletedTask);
        var usageBilling = new Mock<IUsageBillingService>();
        usageBilling.Setup(service => service.CanProcessAsync(
                businessId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageGateResult(true, "allowed", "allowed", null));

        var service = new ConversationFollowUpService(
            stateRepository.Object,
            stateManager.Object,
            conversations.Object,
            messages.Object,
            inbound.Object,
            configs.Object,
            facts.Object,
            renderer.Object,
            Mock.Of<IMessageSequenceResolver>(),
            dispatcher.Object,
            Mock.Of<IBusinessClock>(),
            Mock.Of<IWorkingHoursService>(),
            usageBilling.Object,
            NullLogger<ConversationFollowUpService>.Instance);

        return new Fixture(
            service,
            state,
            conversation,
            source,
            config,
            stateManager,
            renderer,
            dispatcher);
    }

    private sealed record Fixture(
        ConversationFollowUpService Service,
        ConversationState State,
        Conversation Conversation,
        Message Source,
        AgentConfig Config,
        Mock<IConversationStateManager> StateManager,
        Mock<IDeterministicResponseRenderer> Renderer,
        Mock<IOutboundMessageDispatcher> Dispatcher);
}
