using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Conversation = Auraly.Platform.Domain.Entities.Conversation;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

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
            Mock.Of<IConversationService>(),
            Mock.Of<IConversationFactsService>(),
            Mock.Of<IMessageService>(),
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
                        Metadata = new WebhookMetadata { PhoneNumberId = "receiver-san-martin" },
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
        message.RecipientPhoneNumberId.Should().Be("receiver-san-martin");
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ForwardsReliableAudioToMessagePipeline()
    {
        var quality = new AudioTranscriptionQualityAssessment(
            AudioTranscriptionReliability.Reliable, 0.91m, true, "ok", []);
        var (parser, whatsApp, _, _, _) = AudioParser("quiero dos perniles", quality);
        var businessId = Guid.NewGuid();

        var messages = (await parser.ExtractAllMessagesFromEntryAsync(VoiceEntry(), businessId)).ToList();

        messages.Should().ContainSingle();
        messages[0].MessageText.Should().Be("quiero dos perniles");
        whatsApp.Verify(
            service => service.SendTextMessageAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_AsksSpecificConfirmationForAmbiguousAudio()
    {
        var quality = new AudioTranscriptionQualityAssessment(
            AudioTranscriptionReliability.Ambiguous, 0.45m, false,
            "low_average_log_probability", ["low_average_log_probability"]);
        var options = new AudioTranscriptionQualityOptions
        {
            AmbiguousAudioReply = "Escuche: {{transcription}}. Me confirmas?"
        };
        var (parser, whatsApp, _, _, _) = AudioParser("quiero dos perniles", quality, options);
        var businessId = Guid.NewGuid();

        var messages = (await parser.ExtractAllMessagesFromEntryAsync(VoiceEntry(), businessId)).ToList();

        messages.Should().BeEmpty();
        whatsApp.Verify(
            service => service.SendTextMessageAsync(
                businessId, "573001112233", "Escuche: quiero dos perniles. Me confirmas?"),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_UsesGenericReplyForUnreliableAudio()
    {
        var quality = new AudioTranscriptionQualityAssessment(
            AudioTranscriptionReliability.Unreliable, 0.10m, false,
            "very_high_no_speech_probability", ["very_high_no_speech_probability"]);
        var options = new AudioTranscriptionQualityOptions
        {
            UnclearAudioReply = "No pude entender el audio."
        };
        var (parser, whatsApp, _, _, _) = AudioParser("texto posiblemente inventado", quality, options);
        var businessId = Guid.NewGuid();

        var messages = (await parser.ExtractAllMessagesFromEntryAsync(VoiceEntry(), businessId)).ToList();

        messages.Should().BeEmpty();
        whatsApp.Verify(
            service => service.SendTextMessageAsync(
                businessId, "573001112233", "No pude entender el audio."),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ConfirmedAmbiguousAudio_ForwardsOriginalTranscription()
    {
        const string transcription = "Bueno, para el jamon cuny dame 6. Para el maiz dame 2 maiz congelado. Para las tocinetas dame 3 salsa tocineta.";
        var quality = new AudioTranscriptionQualityAssessment(
            AudioTranscriptionReliability.Ambiguous, 0.45m, false,
            "low_average_log_probability", ["low_average_log_probability"]);
        var (parser, _, facts, _, persistedMessages) = AudioParser(transcription, quality);
        var businessId = Guid.NewGuid();

        var audioMessages = (await parser.ExtractAllMessagesFromEntryAsync(
            VoiceEntry(), businessId)).ToList();
        var confirmedMessages = (await parser.ExtractAllMessagesFromEntryAsync(
            TextEntry("Si"), businessId)).ToList();

        audioMessages.Should().BeEmpty();
        confirmedMessages.Should().ContainSingle();
        confirmedMessages[0].MessageText.Should().Be(transcription);
        confirmedMessages[0].ProviderMessageId.Should().Be("wamid.text");
        facts.Verify(service => service.SetAsync(
            It.IsAny<Guid>(), businessId,
            WhatsAppWebhookParserService.PendingAudioConfirmationFactKey,
            It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
        facts.Verify(service => service.ClearFieldsAsync(
            It.IsAny<Guid>(),
            It.Is<IReadOnlyCollection<string>>(keys => keys.Contains(
                WhatsAppWebhookParserService.PendingAudioConfirmationFactKey)),
            It.IsAny<CancellationToken>()), Times.Once);
        persistedMessages.Verify(service => service.SaveMessageAsync(
            It.IsAny<Guid>(), "Bot", It.Is<string>(value => value.Contains(transcription))), Times.Once);
    }

    private static (WhatsAppWebhookParserService Parser, Mock<IWhatsAppService> WhatsApp,
        Mock<IConversationFactsService> Facts, Mock<IConversationService> Conversations,
        Mock<IMessageService> Messages) AudioParser(
        string transcriptionText,
        AudioTranscriptionQualityAssessment quality,
        AudioTranscriptionQualityOptions? options = null)
    {
        var whatsApp = new Mock<IWhatsAppService>();
        whatsApp.Setup(service => service.DownloadMediaAsync(It.IsAny<Guid>(), "media-1"))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        var ai = new Mock<IAIService>();
        var transcription = new AudioTranscriptionResult(
            transcriptionText,
            TimeSpan.FromSeconds(3),
            []);
        ai.Setup(service => service.TranscribeAudioAsync(It.IsAny<Stream>(), "audio/ogg"))
            .ReturnsAsync(transcription);

        var evaluator = new Mock<IAudioTranscriptionQualityEvaluator>();
        evaluator.Setup(service => service.Evaluate(transcription)).Returns(quality);

        var conversationId = Guid.NewGuid();
        var conversations = new Mock<IConversationService>();
        conversations
            .Setup(service => service.GetOrCreateConversationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid businessId, string userNumber, string? customerName) => new Conversation
            {
                ConversationId = conversationId,
                BusinessId = businessId,
                UserNumber = userNumber,
                CustomerName = customerName
            });
        string? storedFact = null;
        var facts = new Mock<IConversationFactsService>();
        facts.Setup(service => service.SetAsync(
                conversationId, It.IsAny<Guid>(),
                WhatsAppWebhookParserService.PendingAudioConfirmationFactKey,
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, string _, string value, bool _, CancellationToken _) => storedFact = value)
            .Returns(Task.CompletedTask);
        facts.Setup(service => service.GetAsync(
                conversationId, WhatsAppWebhookParserService.PendingAudioConfirmationFactKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedFact);
        facts.Setup(service => service.ClearFieldsAsync(
                conversationId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => storedFact = null)
            .ReturnsAsync([WhatsAppWebhookParserService.PendingAudioConfirmationFactKey]);
        var persistedMessages = new Mock<IMessageService>();

        var resolvedOptions = options ?? new AudioTranscriptionQualityOptions();
        return (
            new WhatsAppWebhookParserService(
                whatsApp.Object,
                ai.Object,
                evaluator.Object,
                resolvedOptions,
                Mock.Of<IBusinessIdentificationService>(),
                conversations.Object,
                facts.Object,
                persistedMessages.Object,
                NullLogger<WhatsAppWebhookParserService>.Instance),
            whatsApp,
            facts,
            conversations,
            persistedMessages);
    }

    private static Entry VoiceEntry() => new()
    {
        Changes =
        [
            new Change
            {
                Field = "messages",
                Value = new Value
                {
                    Messages =
                    [
                        new Message
                        {
                            Id = "wamid.audio",
                            From = "573001112233",
                            Type = "voice",
                            Voice = new VoiceMessage
                            {
                                Id = "media-1",
                                MimeType = "audio/ogg"
                            }
                        }
                    ]
                }
            }
        ]
    };

    private static Entry TextEntry(string text) => new()
    {
        Changes =
        [
            new Change
            {
                Field = "messages",
                Value = new Value
                {
                    Contacts =
                    [
                        new Contact { Profile = new Profile { Name = "Richard" } }
                    ],
                    Messages =
                    [
                        new Auraly.Platform.Application.DTOs.Message
                        {
                            Id = "wamid.text",
                            From = "573001112233",
                            Type = "text",
                            Text = new TextMessage { Body = text }
                        }
                    ]
                }
            }
        ]
    };
}
