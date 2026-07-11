using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class InboundInteractivePayloadCaseSensitivityTests
{
    [Fact]
    public async Task ProcessAsync_PayloadsThatDifferByCase_AreDistinctActions()
    {
        var businessId = Guid.NewGuid();
        const string user = "573001112233";
        var messages = new List<IncomingMessage>
        {
            new()
            {
                UserNumber = user,
                ProviderMessageId = "wamid.one",
                InteractivePayload = "catalog:select:ProductA",
                MessageText = "Producto A"
            },
            new()
            {
                UserNumber = user,
                ProviderMessageId = "wamid.two",
                InteractivePayload = "catalog:select:producta",
                MessageText = "Producto a"
            }
        };
        var payloads = new List<string?>();
        var messageProcessor = new Mock<IWhatsAppMessageProcessorService>();
        messageProcessor.Setup(value => value.ProcessIncomingMessageAsync(
                businessId,
                user,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Callback<Guid, string, string, string?, AgentInboundMetadata?>((_, _, _, _, metadata) =>
                payloads.Add(metadata?.InteractivePayload))
            .Returns(Task.CompletedTask);
        var processor = new InboundMessageBatchProcessor(
            messageProcessor.Object,
            NullLogger<InboundMessageBatchProcessor>.Instance);

        var result = await processor.ProcessAsync(businessId, messages, CancellationToken.None);

        Assert.Equal(2, result.InteractiveMessageCount);
        Assert.Equal(2, payloads.Count);
        Assert.Equal("catalog:select:ProductA", payloads[0]);
        Assert.Equal("catalog:select:producta", payloads[1]);
    }
}
