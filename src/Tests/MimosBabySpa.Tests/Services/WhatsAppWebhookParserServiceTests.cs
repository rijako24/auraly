using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class WhatsAppWebhookParserServiceTests
{
    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ParsesTemplateButtonPayload()
    {
        var parser = new WhatsAppWebhookParserService(
            Mock.Of<IWhatsAppService>(),
            Mock.Of<IAIService>(),
            new AudioTranscriptionQualityEvaluator(new AudioTranscriptionQualityOptions()),
            new AudioTranscriptionQualityOptions(),
            Mock.Of<IBusinessIdentificationService>(),
            NullLogger<WhatsAppWebhookParserService>.Instance);
        var attemptId = Guid.NewGuid();
        var entry = new Entry
        {
            Id = "entry-id",
            Changes =
            [
                new Change
                {
                    Field = "messages",
                    Value = new Value
                    {
                        Contacts =
                        [
                            new Contact { Profile = new Profile { Name = "Geral" } }
                        ],
                        Messages =
                        [
                            new Message
                            {
                                Id = "wamid.inbound",
                                From = "573042052007",
                                Type = "button",
                                Button = new ButtonMessage
                                {
                                    Text = "Aceptar",
                                    Payload = $"external_interaction:accepted:{attemptId}"
                                },
                                Context = new MessageContext { Id = "wamid.outbound" }
                            }
                        ]
                    }
                }
            ]
        };

        var messages = (await parser.ExtractAllMessagesFromEntryAsync(entry, Guid.NewGuid())).ToList();

        messages.Should().ContainSingle();
        var message = messages[0];
        message.UserNumber.Should().Be("573042052007");
        message.MessageText.Should().Be("Aceptar");
        message.InteractivePayload.Should().Be($"external_interaction:accepted:{attemptId}");
        message.ReplyToProviderMessageId.Should().Be("wamid.outbound");
        message.ProviderMessageId.Should().Be("wamid.inbound");
        message.CustomerName.Should().Be("Geral");
    }
}