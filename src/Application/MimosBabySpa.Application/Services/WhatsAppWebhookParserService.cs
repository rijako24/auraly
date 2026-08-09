using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class WhatsAppWebhookParserService : IWhatsAppWebhookParserService
{
    internal const string PendingAudioConfirmationFactKey = "system.pending_audio_confirmation";

    private readonly IWhatsAppService _whatsAppService;
    private readonly IAIService _aiService;
    private readonly IBusinessIdentificationService _businessIdentificationService;
    private readonly IAudioTranscriptionQualityEvaluator _audioQualityEvaluator;
    private readonly AudioTranscriptionQualityOptions _audioQualityOptions;
    private readonly IConversationService _conversationService;
    private readonly IConversationFactsService _conversationFacts;
    private readonly IMessageService _messageService;
    private readonly ILogger<WhatsAppWebhookParserService> _logger;
    private readonly IInboundDocumentTextExtractor? _documentTextExtractor;

    public WhatsAppWebhookParserService(
        IWhatsAppService whatsAppService,
        IAIService aiService,
        IAudioTranscriptionQualityEvaluator audioQualityEvaluator,
        AudioTranscriptionQualityOptions audioQualityOptions,
        IBusinessIdentificationService businessIdentificationService,
        IConversationService conversationService,
        IConversationFactsService conversationFacts,
        IMessageService messageService,
        ILogger<WhatsAppWebhookParserService> logger,
        IInboundDocumentTextExtractor? documentTextExtractor = null)
    {
        _whatsAppService = whatsAppService;
        _aiService = aiService;
        _businessIdentificationService = businessIdentificationService;
        _logger = logger;
        _audioQualityEvaluator = audioQualityEvaluator;
        _audioQualityOptions = audioQualityOptions;
        _conversationService = conversationService;
        _conversationFacts = conversationFacts;
        _messageService = messageService;
        _documentTextExtractor = documentTextExtractor;
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
            var recipientPhoneNumberId = change.Value.Metadata?.PhoneNumberId;

            foreach (var message in change.Value.Messages)
            {
                // Mensaje de texto
                if (message.Type == "text" && message.Text != null)
                {
                    var resolvedText = await ResolvePendingAudioConfirmationAsync(
                        businessId, message.From, customerName, message.Text.Body);
                    if (resolvedText is null)
                        continue;

                    result.Add(new IncomingMessage
                    {
                        UserNumber = message.From,
                        MessageText = resolvedText,
                        CustomerName = customerName,
                        ProviderMessageId = message.Id,
                        ReplyToProviderMessageId = message.Context?.Id,
                        Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        RecipientPhoneNumberId = recipientPhoneNumberId
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
                        InteractivePayload = interactiveReply.Id,
                        RecipientPhoneNumberId = recipientPhoneNumberId
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
                        InteractivePayload = message.Button.Payload,
                        RecipientPhoneNumberId = recipientPhoneNumberId
                    });
                }
                else if ((message.Type == "image" && message.Image != null)
                         || (message.Type == "document" && message.Document != null))
                {
                    var inbound = await ExtractInboundMediaAsync(
                        message, businessId, customerName, recipientPhoneNumberId);
                    if (inbound is not null)
                        result.Add(inbound);
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
                                Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                                RecipientPhoneNumberId = recipientPhoneNumberId
                            });

                            _logger.LogInformation(
                                "Audio aceptado para {UserNumber}: Reliability={Reliability}, Confidence={Confidence}, Reason={Reason}",
                                message.From,
                                quality.Reliability,
                                quality.ConfidenceScore,
                                quality.Reason);
                        }
                        else if (quality.Reliability == AudioTranscriptionReliability.Ambiguous
                                 && !string.IsNullOrWhiteSpace(transcribedText))
                        {
                            _logger.LogInformation(
                                "Audio ambiguo requiere confirmacion para {UserNumber}: Confidence={Confidence}, Reason={Reason}, Text={Transcription}",
                                message.From,
                                quality.ConfidenceScore,
                                quality.Reason,
                                transcribedText);

                            var conversation = await _conversationService.GetOrCreateConversationAsync(
                                businessId, message.From, customerName);
                            var pending = new PendingAudioConfirmation(
                                transcribedText,
                                DateTime.UtcNow.AddMinutes(Math.Clamp(
                                    _audioQualityOptions.AmbiguousConfirmationTtlMinutes, 1, 60)));
                            await _conversationFacts.SetAsync(conversation.ConversationId, businessId,
                                PendingAudioConfirmationFactKey, JsonSerializer.Serialize(pending));
                            await SendAmbiguousAudioReplyAsync(
                                businessId, message.From, transcribedText, conversation.ConversationId);
                        }
                        else if (!string.IsNullOrWhiteSpace(transcribedText))
                        {
                            _logger.LogWarning(
                                "Audio no confiable rechazado para {UserNumber}: Reliability={Reliability}, Confidence={Confidence}, Reason={Reason}, Text={Transcription}",
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

    private async Task<IncomingMessage?> ExtractInboundMediaAsync(
        Message message,
        Guid businessId,
        string? customerName,
        string? recipientPhoneNumberId)
    {
        var mediaId = message.Image?.Id ?? message.Document?.Id ?? string.Empty;
        var mimeType = message.Image?.MimeType ?? message.Document?.MimeType;
        var fileName = message.Document?.FileName;
        var caption = message.Image?.Caption ?? message.Document?.Caption;

        if (_documentTextExtractor is null
            || string.IsNullOrWhiteSpace(mediaId)
            || !_documentTextExtractor.Supports(fileName, mimeType))
        {
            _logger.LogInformation(
                "Se ignora medio no compatible de {UserNumber}: {MimeType} {FileName}",
                message.From, mimeType, fileName);
            return null;
        }

        try
        {
            using var mediaStream = await _whatsAppService.DownloadMediaAsync(businessId, mediaId);
            var extracted = await _documentTextExtractor.ExtractTextAsync(
                mediaStream, fileName, mimeType);
            if (string.IsNullOrWhiteSpace(extracted))
                return null;

            var body = new StringBuilder();
            body.AppendLine("Documento recibido por WhatsApp. Texto extraido:");
            if (!string.IsNullOrWhiteSpace(caption))
                body.AppendLine($"Indicacion del remitente: {caption.Trim()}");
            body.Append(extracted);

            var facts = message.Facts
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            facts["system.inbound_media_type"] = message.Type;
            return new IncomingMessage
            {
                UserNumber = message.From,
                MessageText = body.ToString(),
                CustomerName = customerName,
                ProviderMessageId = message.Id,
                ReplyToProviderMessageId = message.Context?.Id,
                Facts = facts,
                RecipientPhoneNumberId = recipientPhoneNumberId
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "No se pudo extraer texto del medio {MediaId} enviado por {UserNumber}",
                mediaId,
                message.From);
            return null;
        }
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

    private async Task<string?> ResolvePendingAudioConfirmationAsync(
        Guid businessId,
        string userNumber,
        string? customerName,
        string messageText)
    {
        var confirmation = ClassifyConfirmation(messageText);
        if (confirmation == AudioConfirmationAnswer.None)
            return messageText;

        var conversation = await _conversationService.GetOrCreateConversationAsync(
            businessId, userNumber, customerName);
        var json = await _conversationFacts.GetAsync(
            conversation.ConversationId, PendingAudioConfirmationFactKey);
        if (string.IsNullOrWhiteSpace(json))
            return messageText;

        PendingAudioConfirmation? pending;
        try
        {
            pending = JsonSerializer.Deserialize<PendingAudioConfirmation>(json);
        }
        catch (JsonException)
        {
            pending = null;
        }

        await _conversationFacts.ClearFieldsAsync(
            conversation.ConversationId, [PendingAudioConfirmationFactKey]);

        if (pending is null || pending.ExpiresAtUtc <= DateTime.UtcNow)
            return messageText;

        if (confirmation == AudioConfirmationAnswer.Affirmative)
        {
            _logger.LogInformation(
                "Confirmacion de audio aplicada para ConversationId={ConversationId}",
                conversation.ConversationId);
            return pending.Transcription;
        }

        await SendUnclearAudioReplyAsync(businessId, userNumber);
        return null;
    }

    private static AudioConfirmationAnswer ClassifyConfirmation(string text)
    {
        var normalized = NormalizeConfirmation(text);
        if (normalized is "si" or "correcto" or "correcta" or "exacto" or "exacta"
            or "eso es" or "asi es" or "confirmo" or "confirmado")
            return AudioConfirmationAnswer.Affirmative;
        if (normalized is "no" or "no es" or "incorrecto" or "incorrecta")
            return AudioConfirmationAnswer.Negative;
        return AudioConfirmationAnswer.None;
    }

    private static string NormalizeConfirmation(string value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', builder.ToString().Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
    private async Task SendAmbiguousAudioReplyAsync(
        Guid businessId,
        string userNumber,
        string transcription,
        Guid conversationId)
    {
        if (string.IsNullOrWhiteSpace(_audioQualityOptions.AmbiguousAudioReply))
        {
            await SendUnclearAudioReplyAsync(businessId, userNumber);
            return;
        }

        var reply = _audioQualityOptions.AmbiguousAudioReply.Replace(
            "{{transcription}}",
            transcription,
            StringComparison.OrdinalIgnoreCase);

        await _messageService.SaveMessageAsync(conversationId, "Bot", reply);
        await _whatsAppService.SendTextMessageAsync(businessId, userNumber, reply);
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

    private sealed record PendingAudioConfirmation(string Transcription, DateTime ExpiresAtUtc);

    private enum AudioConfirmationAnswer { None, Affirmative, Negative }

    private sealed record ResolvedInteractiveReply(string Id, string Title);
}
