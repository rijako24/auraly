using MimosBabySpa.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class WhatsAppWebhookParserService : IWhatsAppWebhookParserService
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAIService _aiService;
    private readonly IBusinessIdentificationService _businessIdentificationService;
    private readonly ILogger<WhatsAppWebhookParserService> _logger;

    public WhatsAppWebhookParserService(
        IWhatsAppService whatsAppService,
        IAIService aiService,
        IBusinessIdentificationService businessIdentificationService,
        ILogger<WhatsAppWebhookParserService> logger)
    {
        _whatsAppService = whatsAppService;
        _aiService = aiService;
        _businessIdentificationService = businessIdentificationService;
        _logger = logger;
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
                else if (message.Type == "interactive" && message.Interactive?.ButtonReply != null)
                {
                    result.Add(new IncomingMessage
                    {
                        UserNumber = message.From,
                        MessageText = message.Interactive.ButtonReply.Title,
                        CustomerName = customerName,
                        ProviderMessageId = message.Id,
                        ReplyToProviderMessageId = message.Context?.Id,
                        Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        InteractivePayload = message.Interactive.ButtonReply.Id
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
                        var transcribedText = await _aiService.TranscribeAudioAsync(audioStream, mimeType);

                        if (!string.IsNullOrWhiteSpace(transcribedText))
                        {
                            result.Add(new IncomingMessage
                            {
                                UserNumber = message.From,
                                MessageText = transcribedText,
                                CustomerName = customerName,
                                ProviderMessageId = message.Id,
                                ReplyToProviderMessageId = message.Context?.Id,
                                Facts = message.Facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            });

                            _logger.LogInformation("Audio transcrito para {UserNumber}: {Transcription}",
                                message.From, transcribedText);
                        }
                        else
                        {
                            _logger.LogWarning("No se pudo transcribir el audio del usuario {UserNumber}", message.From);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error transcribiendo audio del usuario {UserNumber}", message.From);
                        // Continuar con los demás mensajes
                    }
                }
            }
        }

        return result;
    }
}
