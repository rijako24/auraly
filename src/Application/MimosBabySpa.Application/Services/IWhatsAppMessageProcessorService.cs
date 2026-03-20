using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio principal para procesar mensajes de WhatsApp.
/// Encapsula toda la lógica de negocio relacionada con el procesamiento de mensajes.
/// </summary>
public interface IWhatsAppMessageProcessorService
{
    /// <summary>
    /// Verifica el webhook de WhatsApp (para la validación inicial)
    /// </summary>
    Task<string?> VerifyWebhookAsync(string mode, string token, string challenge);

    /// <summary>
    /// Procesa un mensaje entrante de WhatsApp (texto) usando el Generic Flow Engine.
    /// <paramref name="businessContext"/> debe incluir <see cref="BusinessContext.AgentId"/> del canal.
    /// </summary>
    Task ProcessIncomingMessageAsync(
        BusinessContext businessContext,
        string userNumber,
        string messageText,
        string? customerName = null);
}
