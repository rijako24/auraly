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

    [Fact]
    public async Task ExtractAllMessagesFromEntryAsync_ForwardsReliableAudioToMessagePipeline()
    {
        var quality = new AudioTranscriptionQualityAssessment(
            AudioTranscriptionReliability.Reliable, 0.91m, true, "ok", []);
        var (parser, whatsApp) = AudioParser("quiero dos perniles", quality);
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
        var (parser, whatsApp) = AudioParser("quiero dos perniles", quality, options);
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
        var (parser, whatsApp) = AudioParser("texto posiblemente inventado", quality, options);
        var businessId = Guid.NewGuid();

        var messages = (await parser.ExtractAllMessagesFromEntryAsync(VoiceEntry(), businessId)).ToList();

        messages.Should().BeEmpty();
        whatsApp.Verify(
            service => service.SendTextMessageAsync(
                businessId, "573001112233", "No pude entender el audio."),
            Times.Once);
    }

    private static (WhatsAppWebhookParserService Parser, Mock<IWhatsAppService> WhatsApp) AudioParser(
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

        var resolvedOptions = options ?? new AudioTranscriptionQualityOptions();
        return (
            new WhatsAppWebhookParserService(
                whatsApp.Object,
                ai.Object,
                evaluator.Object,
                resolvedOptions,
                Mock.Of<IBusinessIdentificationService>(),
                NullLogger<WhatsAppWebhookParserService>.Instance),
            whatsApp);
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
    };}