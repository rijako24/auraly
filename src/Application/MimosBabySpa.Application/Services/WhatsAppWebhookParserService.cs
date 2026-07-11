using System.Globalization;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class WhatsAppWebhookParserService : IWhatsAppWebhookParserService
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAIService _aiService;
    private readonly IBusinessIdentificationService _businessIdentificationService;
    private readonly IAudioTranscriptionQualityEvaluator _audioQualityEvaluator;
    private readonly AudioTranscriptionQualityOptions _audioQualityOptions;
    private readonly ILogger<WhatsAppWebhookParserService> _logger;

    public WhatsAppWebhookParserService(
        IWhatsAppService whatsAppService,
        IAIService aiService,
        IAudioTranscriptionQualityEvaluator audioQualityEvaluator,
        AudioTranscriptionQualityOptions audioQualityOptions,
        IBusinessIdentificationService businessIdentificationService,
        ILogger<WhatsAppWebhookParserService> logger)
    {
        _whatsAppService = whatsAppService;
        _aiService = aiService;
        _businessIdentificationService = businessIdentificationService;
        _logger = logger;
        _audioQualityEvaluator = audioQualityEvaluator;
        _audioQualityOptions = audioQualityOptions;
    }

    public async Task<IEnumerable<IncomingMessage>> ExtractAllMessagesFromEntryAsync(Entry entry, Guid businessId)
    {
        var result = new List<IncomingMessage>();

        if (entry?.Changes == null || !entry.Changes.Any())
        {
            return result;
        }

        foreach (var change in entry.Changes)
        {
            if (change.Field != "messages" || change.Value.Messages == null)
                continue;

            var customerName = change.Value.Contacts?.FirstOrDefault()?.Profile?.Name;

            foreach (var message in change.Value.Messages)
            {
                // Mensaje de texto
                if (message.Type == "text" && message.Text != null)
                {
                    result.Add(new IncomingMessage
                    {
                        UserNumber = message.From,
                        MessageText = message.Text.Body,
                        CustomerName = customerName,
                        ProviderMessageId = message.Id,
                        ReplyToProviderMessageId = message.Context?.Id,
                        Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    });
                }
                else if (message.Type == "interactive"
                         && TryResolveInteractiveReply(message.Interactive, out var interactiveReply))
                {
                    result.Add(new IncomingMessage
                    {
                        UserNumber = message.From,
                        MessageText = interactiveReply.Title,
                        CustomerName = customerName,
                        ProviderMessageId = message.Id,
                        ReplyToProviderMessageId = message.Context?.Id,
                        Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        InteractivePayload = interactiveReply.Id
                    });
                }
                else if (message.Type == "button" && message.Button != null)
                {
                    result.Add(new IncomingMessage
                    {
                        UserNumber = message.From,
                        MessageText = message.Button.Text,
                        CustomerName = customerName,
                        ProviderMessageId = message.Id,
                        ReplyToProviderMessageId = message.Context?.Id,
                        Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        InteractivePayload = message.Button.Payload
                    });
                }
                // Mensaje de voz (voice) o audio - transcribir
                else if ((message.Type == "voice" && message.Voice != null) ||
                         (message.Type == "audio" && message.Audio != null))
                {
                    try
                    {
                        string mediaId;
                        string mimeType;
                        
                        if (message.Type == "voice" && message.Voice != null)
                        {
                            mediaId = message.Voice.Id;
                            mimeType = message.Voice.MimeType;
                        }
                        else if (message.Audio != null)
                        {
                            mediaId = message.Audio.Id;
                            mimeType = message.Audio.MimeType;
                        }
                        else
                        {
                            continue; // No debería llegar aquí, pero por seguridad
                        }

                        _logger.LogInformation("Transcribiendo {Type} de {UserNumber}, MediaId: {MediaId}",
                            message.Type, message.From, mediaId);

                        // Descargar el audio
                        using var audioStream = await _whatsAppService.DownloadMediaAsync(businessId, mediaId);

                        // Transcribir el audio a texto
                        var transcription = await _aiService.TranscribeAudioAsync(audioStream, mimeType);
                        var quality = _audioQualityEvaluator.Evaluate(transcription);
                        var transcribedText = transcription.Text?.Trim() ?? string.Empty;

                        if (quality.ShouldAccept && !string.IsNullOrWhiteSpace(transcribedText))
                        {
                            result.Add(new IncomingMessage
                            {
                                UserNumber = message.From,
                                MessageText = transcribedText,
                                CustomerName = customerName,
                                ProviderMessageId = message.Id,
                                ReplyToProviderMessageId = message.Context?.Id,
                                Facts = BuildAudioFacts(message.Facts, quality)
                            });

                            _logger.LogInformation(
                                "Audio aceptado para {UserNumber}: Reliability={Reliability}, Confidence={Confidence}, Reason={Reason}",
                                message.From,
                                quality.Reliability,
                                quality.ConfidenceScore,
                                quality.Reason);
                        }
                        else if (!string.IsNullOrWhiteSpace(transcribedText))
                        {
                            _logger.LogWarning(
                                "Audio rechazado para {UserNumber}: Reliability={Reliability}, Confidence={Confidence}, Reason={Reason}, Text={Transcription}",
                                message.From,
                                quality.Reliability,
                                quality.ConfidenceScore,
                                quality.Reason,
                                transcribedText);

                            await SendUnclearAudioReplyAsync(businessId, message.From);
                        }
                        else
                        {
                            _logger.LogWarning("No se pudo transcribir el audio del usuario {UserNumber}", message.From);
                            await SendUnclearAudioReplyAsync(businessId, message.From);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error transcribiendo audio del usuario {UserNumber}", message.From);
                        await TrySendUnclearAudioReplyAsync(businessId, message.From);
                        // Continuar con los demás mensajes
                    }
                }
            }
        }

        return result;
    }

    private static bool TryResolveInteractiveReply(
        InteractiveMessage? interactive,
        out ResolvedInteractiveReply reply)
    {
        reply = default!;
        if (interactive?.ButtonReply is { } button
            && TryNormalizeInteractiveReply(button.Id, button.Title, out reply))
        {
            return true;
        }

        return interactive?.ListReply is { } list
            && TryNormalizeInteractiveReply(list.Id, list.Title, out reply);
    }

    private static bool TryNormalizeInteractiveReply(
        string? id,
        string? title,
        out ResolvedInteractiveReply reply)
    {
        reply = default!;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var normalizedId = id.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? normalizedId : title.Trim();
        reply = new ResolvedInteractiveReply(normalizedId, normalizedTitle);
        return true;
    }
    private async Task SendUnclearAudioReplyAsync(Guid businessId, string userNumber)
    {
        if (string.IsNullOrWhiteSpace(_audioQualityOptions.UnclearAudioReply))
            return;

        await _whatsAppService.SendTextMessageAsync(
            businessId,
            userNumber,
            _audioQualityOptions.UnclearAudioReply);
    }

    private async Task TrySendUnclearAudioReplyAsync(Guid businessId, string userNumber)
    {
        try
        {
            await SendUnclearAudioReplyAsync(businessId, userNumber);
        }
        catch (Exception replyEx)
        {
            _logger.LogWarning(replyEx, "No se pudo enviar respuesta de audio no claro al usuario {UserNumber}", userNumber);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildAudioFacts(
        IReadOnlyDictionary<string, string>? source,
        AudioTranscriptionQualityAssessment quality)
    {
        var facts = source is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);

        facts["system.input.modality"] = "audio";
        facts["system.audio.reliability"] = quality.Reliability.ToString().ToLowerInvariant();
        facts["system.audio.confidence"] = quality.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(quality.Reason))
            facts["system.audio.reason"] = quality.Reason;

        return facts;
    }
    private sealed record ResolvedInteractiveReply(string Id, string Title);
}
