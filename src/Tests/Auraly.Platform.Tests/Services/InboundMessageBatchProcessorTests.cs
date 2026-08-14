using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class InboundMessageBatchProcessorTests
{
    [Fact]
    public async Task ProcessAsync_WhenTextAndInteractiveMessageArriveTogether_ProcessesInteractiveMessageBeforeText()
    {
        var businessId = Guid.NewGuid();
        const string userNumber = "573023823535";
        const string payload = "external_interaction:accepted:1559bd32-ec0b-4356-b98e-d2e754391c29";
        var messages = new List<IncomingMessage>
        {
            new()
            {
                UserNumber = userNumber,
                CustomerName = "Supervoy",
                ProviderMessageId = "wamid.greeting",
                MessageText = "SuperVoy auto reply"
            },
            new()
            {
                UserNumber = userNumber,
                CustomerName = "Supervoy",
                ProviderMessageId = "wamid.accept",
                ReplyToProviderMessageId = "wamid.assignment",
                InteractivePayload = payload,
                MessageText = "Aceptar"
            }
        };

        var calls = new List<ProcessedMessageCall>();
        var messageProcessor = new Mock<IWhatsAppMessageProcessorService>();
        messageProcessor
            .Setup(p => p.ProcessIncomingMessageAsync(
                businessId,
                userNumber,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Callback<Guid, string, string, string?, AgentInboundMetadata?>((_, _, text, customerName, metadata) =>
                calls.Add(new ProcessedMessageCall(text, customerName, metadata)))
            .Returns(Task.CompletedTask);

        var processor = new InboundMessageBatchProcessor(
            messageProcessor.Object,
            NullLogger<InboundMessageBatchProcessor>.Instance);

        var result = await processor.ProcessAsync(businessId, messages);

        Assert.Equal(2, result.MessageCount);
        Assert.Equal(1, result.InteractiveMessageCount);
        Assert.True(result.SentToConversationProcessor);
        Assert.Equal(2, calls.Count);

        Assert.Equal("Aceptar", calls[0].Text);
        Assert.Equal("Supervoy", calls[0].CustomerName);
        Assert.Equal("wamid.accept", calls[0].Metadata?.ProviderMessageId);
        Assert.Equal("wamid.assignment", calls[0].Metadata?.ReplyToProviderMessageId);
        Assert.Equal(payload, calls[0].Metadata?.InteractivePayload);

        Assert.Equal("SuperVoy auto reply", calls[1].Text);
        Assert.Equal("Supervoy", calls[1].CustomerName);
        Assert.Equal("wamid.greeting", calls[1].Metadata?.ProviderMessageId);
        Assert.Null(calls[1].Metadata?.ReplyToProviderMessageId);
        Assert.Null(calls[1].Metadata?.InteractivePayload);
    }

    [Fact]
    public async Task ProcessAsync_WhenSameInteractivePayloadArrivesTwice_ProcessesItOnce()
    {
        var businessId = Guid.NewGuid();
        const string userNumber = "573023823535";
        const string payload = "external_interaction:accepted:1559bd32-ec0b-4356-b98e-d2e754391c29";
        var messages = new List<IncomingMessage>
        {
            new()
            {
                UserNumber = userNumber,
                ProviderMessageId = "wamid.accept.one",
                ReplyToProviderMessageId = "wamid.assignment",
                InteractivePayload = payload,
                MessageText = "Aceptar"
            },
            new()
            {
                UserNumber = userNumber,
                ProviderMessageId = "wamid.accept.two",
                ReplyToProviderMessageId = "wamid.assignment",
                InteractivePayload = payload,
                MessageText = "Aceptar"
            }
        };

        var calls = new List<ProcessedMessageCall>();
        var messageProcessor = new Mock<IWhatsAppMessageProcessorService>();
        messageProcessor
            .Setup(p => p.ProcessIncomingMessageAsync(
                businessId,
                userNumber,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Callback<Guid, string, string, string?, AgentInboundMetadata?>((_, _, text, customerName, metadata) =>
                calls.Add(new ProcessedMessageCall(text, customerName, metadata)))
            .Returns(Task.CompletedTask);

        var processor = new InboundMessageBatchProcessor(
            messageProcessor.Object,
            NullLogger<InboundMessageBatchProcessor>.Instance);

        var result = await processor.ProcessAsync(businessId, messages);

        Assert.Equal(2, result.MessageCount);
        Assert.Equal(1, result.InteractiveMessageCount);
        Assert.True(result.SentToConversationProcessor);
        Assert.Single(calls);
        Assert.Equal("wamid.accept.two", calls[0].Metadata?.ProviderMessageId);
        Assert.Equal(payload, calls[0].Metadata?.InteractivePayload);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoInteractiveMessage_CombinesTextNormally()
    {
        var businessId = Guid.NewGuid();
        const string userNumber = "573001112233";
        var messages = new List<IncomingMessage>
        {
            new() { UserNumber = userNumber, ProviderMessageId = "wamid.one", MessageText = "Hola" },
            new() { UserNumber = userNumber, ProviderMessageId = "wamid.two", MessageText = "Quiero comprar" }
        };

        var messageProcessor = new Mock<IWhatsAppMessageProcessorService>();
        messageProcessor
            .Setup(p => p.ProcessIncomingMessageAsync(
                businessId,
                userNumber,
                "Hola\nQuiero comprar",
                null,
                It.Is<AgentInboundMetadata?>(m => m != null && m.ProviderMessageId == "wamid.two")))
            .Returns(Task.CompletedTask);

        var processor = new InboundMessageBatchProcessor(
            messageProcessor.Object,
            NullLogger<InboundMessageBatchProcessor>.Instance);

        var result = await processor.ProcessAsync(businessId, messages);

        Assert.Equal(2, result.MessageCount);
        Assert.Equal(0, result.InteractiveMessageCount);
        Assert.True(result.SentToConversationProcessor);
        messageProcessor.VerifyAll();
    }

    private sealed record ProcessedMessageCall(
        string Text,
        string? CustomerName,
        AgentInboundMetadata? Metadata);
}
