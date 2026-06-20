namespace MimosBabySpa.Application.Services;

using MimosBabySpa.Application.Agents;

public interface IWhatsAppService
{
    /// <summary>
    /// Acusa recibo del mensaje: marca como leído y muestra indicador de escritura.
    /// Recibe credenciales ya resueltas — sin consulta a BD. Seguro para fire-and-forget.
    /// Best-effort: no lanza si falla. Dura ~25s o hasta enviar respuesta.
    /// </summary>
    Task AcknowledgeMessageAsync(string phoneNumberId, string accessToken, string whatsAppMessageId);

    Task SendTextMessageAsync(Guid businessId, string to, string message);
    Task<string?> SendButtonMessageAsync(Guid businessId, string to, string message, IReadOnlyList<OutboundButton> buttons);
    Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null);
    Task SendDocumentMessageAsync(Guid businessId, string to, string documentUrl, string? caption = null, string? filename = null);
    Task<bool> VerifyWebhookAsync(string mode, string token, string challenge);
    Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId);
}
