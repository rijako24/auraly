namespace MimosBabySpa.Application.Services;

public interface IWhatsAppService
{
    Task SendTextMessageAsync(Guid businessId, string to, string message);
    Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null);
    Task<bool> VerifyWebhookAsync(string mode, string token, string challenge);
    Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId);
}
