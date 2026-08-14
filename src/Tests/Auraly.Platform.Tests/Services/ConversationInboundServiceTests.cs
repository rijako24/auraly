using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ConversationInboundServiceTests
{
    [Fact]
    public async Task EnqueueAsync_RecordsWhatsAppCompatibleReceiptAndSchedulesQueue()
    {
        var businessId = Guid.NewGuid();
        string? capturedRawEntryJson = null;
        DateTime capturedDueAt = default;

        var deduplication = new Mock<IInboundMessageDeduplicationService>();
        deduplication
            .Setup(x => x.TryRecordReceivedAsync(
                businessId,
                "whatsapp",
                It.IsAny<string>(),
                "573001112233",
                "Maria",
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, string?, string, DateTime, DateTime, CancellationToken>(
                (_, _, _, _, _, raw, _, due, _) =>
                {
                    capturedRawEntryJson = raw;
                    capturedDueAt = due;
                })
            .ReturnsAsync(true);

        var queue = new Mock<IWhatsAppInboundQueueService>();
        var service = new ConversationInboundService(
            deduplication.Object,
            queue.Object,
            NullLogger<ConversationInboundService>.Instance);

        var result = await service.EnqueueAsync(new ConversationInboundRequest(
            businessId,
            "whatsapp",
            "573001112233",
            "Solicitud de demo desde formulario web.",
            "Maria"));

        result.IsNew.Should().BeTrue();
        result.ProviderMessageId.Should().StartWith("whatsapp:");
        capturedRawEntryJson.Should().NotBeNullOrWhiteSpace();

        var entry = JsonSerializer.Deserialize<Entry>(
            capturedRawEntryJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        entry.Should().NotBeNull();
        entry!.Changes.Should().ContainSingle();
        var change = entry.Changes.Single();
        change.Field.Should().Be("messages");
        change.Value.Contacts.Single().Profile.Name.Should().Be("Maria");

        var message = change.Value.Messages.Single();
        message.Id.Should().Be(result.ProviderMessageId);
        message.From.Should().Be("573001112233");
        message.Type.Should().Be("text");
        message.Text!.Body.Should().Be("Solicitud de demo desde formulario web.");

        queue.Verify(x => x.ScheduleDebounceAsync(
            businessId,
            "whatsapp",
            "573001112233",
            result.ProviderMessageId,
            capturedDueAt,
            It.IsAny<CancellationToken>()), Times.Once);

        deduplication.Verify(x => x.MarkQueuedAsync(
            businessId,
            "whatsapp",
            result.ProviderMessageId,
            capturedDueAt,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
