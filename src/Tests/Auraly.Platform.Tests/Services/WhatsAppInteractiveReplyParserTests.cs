using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class WhatsAppInteractiveReplyParserTests
{
    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ParsesInteractiveButtonReply()
    {
        var payload = $"reservation_attendance:confirm:{Guid.NewGuid():D}";
        var entry = EntryWith(new Message
        {
            Id = "wamid.button.inbound",
            From = "573001112233",
            Type = "interactive",
            Interactive = new InteractiveMessage
            {
                Type = "button_reply",
                ButtonReply = new ButtonReply { Id = $"  {payload}  ", Title = "  Confirmar  " }
            },
            Context = new MessageContext { Id = "wamid.button.outbound" }
        });

        var message = (await CreateParser().ExtractAllMessagesFromEntryAsync(entry, Guid.NewGuid()))
            .Should().ContainSingle().Subject;

        message.MessageText.Should().Be("Confirmar");
        message.InteractivePayload.Should().Be(payload);
        message.ProviderMessageId.Should().Be("wamid.button.inbound");
        message.ReplyToProviderMessageId.Should().Be("wamid.button.outbound");
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ParsesInteractiveListReply()
    {
        var payload = "catalog_selection:service:corte-premium";
        var entry = EntryWith(new Message
        {
            Id = "wamid.list.inbound",
            From = "573001112233",
            Type = "interactive",
            Interactive = new InteractiveMessage
            {
                Type = "list_reply",
                ListReply = new ListReply
                {
                    Id = payload,
                    Title = "Corte premium",
                    Description = "45 minutos"
                }
            },
            Context = new MessageContext { Id = "wamid.list.outbound" }
        });

        var message = (await CreateParser().ExtractAllMessagesFromEntryAsync(entry, Guid.NewGuid()))
            .Should().ContainSingle().Subject;

        message.MessageText.Should().Be("Corte premium");
        message.InteractivePayload.Should().Be(payload);
        message.ReplyToProviderMessageId.Should().Be("wamid.list.outbound");
    }

    [Fact]
    public void InteractiveMessage_DeserializesWhatsAppListReplyShape()
    {
        const string json = """
            {
              "type": "list_reply",
              "list_reply": {
                "id": "catalog_selection:service:corte",
                "title": "Corte",
                "description": "30 minutos"
              }
            }
            """;

        var interactive = JsonSerializer.Deserialize<InteractiveMessage>(json);

        interactive.Should().NotBeNull();
        interactive!.ListReply.Should().NotBeNull();
        interactive.ListReply!.Id.Should().Be("catalog_selection:service:corte");
        interactive.ListReply.Description.Should().Be("30 minutos");
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_IgnoresInteractiveReplyWithoutId()
    {
        var entry = EntryWith(new Message
        {
            Id = "wamid.invalid",
            From = "573001112233",
            Type = "interactive",
            Interactive = new InteractiveMessage
            {
                Type = "button_reply",
                ButtonReply = new ButtonReply { Id = " ", Title = "Confirmar" }
            }
        });

        var messages = await CreateParser().ExtractAllMessagesFromEntryAsync(entry, Guid.NewGuid());

        messages.Should().BeEmpty();
    }

    private static WhatsAppWebhookParserService CreateParser() => new(
        Mock.Of<IWhatsAppService>(),
        Mock.Of<IAIService>(),
        new AudioTranscriptionQualityEvaluator(new AudioTranscriptionQualityOptions()),
        new AudioTranscriptionQualityOptions(),
        Mock.Of<IBusinessIdentificationService>(),
        Mock.Of<IConversationService>(),
        Mock.Of<IConversationFactsService>(),
        Mock.Of<IMessageService>(),
        NullLogger<WhatsAppWebhookParserService>.Instance);

    private static Entry EntryWith(Message message) => new()
    {
        Changes =
        [
            new Change
            {
                Field = "messages",
                Value = new Value
                {
                    Contacts = [new Contact { Profile = new Profile { Name = "Richard" } }],
                    Messages = [message]
                }
            }
        ]
    };
}
