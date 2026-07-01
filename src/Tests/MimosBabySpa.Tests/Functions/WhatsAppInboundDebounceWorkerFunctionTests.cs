using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.API.Functions;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Services;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Functions;

public sealed class WhatsAppInboundDebounceWorkerFunctionTests
{
    [Fact]
    public async Task Run_WhenPendingMessagesAreReady_DelegatesBatchAndMarksProcessed()
    {
        var businessId = Guid.NewGuid();
        const string provider = "whatsapp";
        const string userNumber = "573023823535";
        var receivedAt = DateTime.UtcNow.AddSeconds(-10);
        var pending = new List<InboundMessageReceipt>
        {
            Receipt(businessId, provider, "wamid.greeting", userNumber, "greeting", receivedAt),
            Receipt(businessId, provider, "wamid.accept", userNumber, "accept", receivedAt.AddSeconds(2))
        };

        var deduplication = new Mock<IInboundMessageDeduplicationService>();
        deduplication
            .Setup(d => d.GetPendingConversationMessagesAsync(businessId, provider, userNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        deduplication
            .Setup(d => d.MarkProcessingAsync(businessId, provider, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        deduplication
            .Setup(d => d.MarkProcessedAsync(businessId, provider, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var queue = new Mock<IWhatsAppInboundQueueService>();
        var parser = new Mock<IWhatsAppWebhookParserService>();
        parser
            .Setup(p => p.ExtractAllMessagesFromEntryAsync(It.IsAny<Entry>(), businessId))
            .Returns((Entry entry, Guid _) => Task.FromResult(MessagesForEntry(entry, userNumber)));

        var batchProcessor = new Mock<IInboundMessageBatchProcessor>();
        batchProcessor
            .Setup(p => p.ProcessAsync(
                businessId,
                It.Is<IReadOnlyList<IncomingMessage>>(messages =>
                    messages.Count == 2
                    && messages[0].MessageText == "SuperVoy auto reply"
                    && messages[1].MessageText == "Aceptar"
                    && messages[1].InteractivePayload == "external_interaction:accepted:1559bd32-ec0b-4356-b98e-d2e754391c29"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboundMessageBatchProcessResult(2, 1, true));

        var worker = new WhatsAppInboundDebounceWorkerFunction(
            deduplication.Object,
            queue.Object,
            parser.Object,
            batchProcessor.Object,
            Mock.Of<ILogger<WhatsAppInboundDebounceWorkerFunction>>());
        var body = JsonSerializer.Serialize(new WhatsAppInboundDebounceMessage(
            businessId,
            provider,
            userNumber,
            DateTime.UtcNow.AddSeconds(-1)));

        await worker.Run(body, CancellationToken.None);

        batchProcessor.VerifyAll();
        deduplication.Verify(d => d.MarkProcessedAsync(
            businessId,
            provider,
            It.Is<IEnumerable<string>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { "wamid.accept", "wamid.greeting" }.OrderBy(x => x))),
            It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.ScheduleDebounceAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static InboundMessageReceipt Receipt(
        Guid businessId,
        string provider,
        string providerMessageId,
        string userNumber,
        string entryId,
        DateTime receivedAt) =>
        new()
        {
            BusinessId = businessId,
            Provider = provider,
            ProviderMessageId = providerMessageId,
            UserNumber = userNumber,
            CustomerName = "Supervoy",
            RawEntryJson = JsonSerializer.Serialize(new Entry { Id = entryId }),
            Status = "Pending",
            ReceivedAtUtc = receivedAt
        };

    private static IEnumerable<IncomingMessage> MessagesForEntry(Entry entry, string userNumber) =>
        entry.Id switch
        {
            "greeting" =>
            [
                new IncomingMessage
                {
                    UserNumber = userNumber,
                    CustomerName = "Supervoy",
                    ProviderMessageId = "wamid.greeting",
                    MessageText = "SuperVoy auto reply"
                }
            ],
            "accept" =>
            [
                new IncomingMessage
                {
                    UserNumber = userNumber,
                    CustomerName = "Supervoy",
                    ProviderMessageId = "wamid.accept",
                    ReplyToProviderMessageId = "wamid.assignment",
                    InteractivePayload = "external_interaction:accepted:1559bd32-ec0b-4356-b98e-d2e754391c29",
                    MessageText = "Aceptar"
                }
            ],
            _ => []
        };
}