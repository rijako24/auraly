namespace MimosBabySpa.Application.Messaging;

/// <summary>
/// Límites de mensajería WhatsApp Cloud API (Meta).
/// Text body: https://developers.facebook.com/docs/whatsapp/cloud-api/messages/text-messages
/// </summary>
public static class WhatsAppMessageLimits
{
    /// <summary>Máximo de caracteres en el cuerpo de un mensaje de texto saliente.</summary>
    public const int MaxTextBodyChars = 4096;
}
