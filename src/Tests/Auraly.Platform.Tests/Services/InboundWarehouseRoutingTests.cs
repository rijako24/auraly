using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class InboundWarehouseRoutingTests
{
    [Fact]
    public async Task ProcessAsync_RejectsMessagesForDifferentReceivingNumbersInOneBatch()
    {
        var processor = new InboundMessageBatchProcessor(
            Mock.Of<IWhatsAppMessageProcessorService>(),
            NullLogger<InboundMessageBatchProcessor>.Instance);
        var messages = new List<IncomingMessage>
        {
            new() { UserNumber = "573001112233", RecipientPhoneNumberId = "receiver-1", MessageText = "hola" },
            new() { UserNumber = "573001112233", RecipientPhoneNumberId = "receiver-2", MessageText = "papa" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(Guid.NewGuid(), messages));
    }

    [Fact]
    public async Task ProcessAsync_PropagatesReceivingNumberToAgentMetadata()
    {
        AgentInboundMetadata? captured = null;
        var downstream = new Mock<IWhatsAppMessageProcessorService>();
        downstream.Setup(service => service.ProcessIncomingMessageAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Callback<Guid, string, string, string?, AgentInboundMetadata?>((_, _, _, _, metadata) => captured = metadata)
            .Returns(Task.CompletedTask);
        var processor = new InboundMessageBatchProcessor(
            downstream.Object,
            NullLogger<InboundMessageBatchProcessor>.Instance);

        await processor.ProcessAsync(Guid.NewGuid(),
        [
            new IncomingMessage
            {
                UserNumber = "573001112233",
                RecipientPhoneNumberId = "receiver-2",
                MessageText = "dos tocinetas"
            }
        ]);

        Assert.Equal("receiver-2", captured?.RecipientPhoneNumberId);
    }
}
