using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para parsear y extraer información del webhook de WhatsApp
/// </summary>
public interface IWhatsAppWebhookParserService
{
    /// <summary>
    /// Extrae todos los mensajes (texto y audio) del webhook de WhatsApp.
    /// Si hay audios o notas de voz, los transcribe automáticamente.
    /// Devuelve todos los mensajes ya unificados como texto.
    /// </summary>
    Task<IEnumerable<IncomingMessage>> ExtractAllMessagesAsync(WhatsAppWebhookDto webhookData);
    
    /// <summary>
    /// Extrae todos los mensajes (texto y audio) de una entrada específica del webhook.
    /// Si hay audios o notas de voz, los transcribe automáticamente.
    /// Devuelve todos los mensajes ya unificados como texto.
    /// </summary>
    Task<IEnumerable<IncomingMessage>> ExtractAllMessagesFromEntryAsync(Entry entry);
}
