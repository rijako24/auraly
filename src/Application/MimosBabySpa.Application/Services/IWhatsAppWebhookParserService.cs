using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para parsear y extraer información del webhook de WhatsApp
/// </summary>
public interface IWhatsAppWebhookParserService
{
    /// <summary>
    /// Extrae todos los mensajes (texto y audio) de una entrada específica del webhook.
    /// Si hay audios o notas de voz, los transcribe automáticamente.
    /// Devuelve todos los mensajes ya unificados como texto.
    /// </summary>
    /// <param name="entry">Entrada del webhook</param>
    /// <param name="businessId">ID del negocio (para resolver credenciales en transcripción de audio)</param>
    Task<IEnumerable<IncomingMessage>> ExtractAllMessagesFromEntryAsync(Entry entry, Guid businessId);
}
