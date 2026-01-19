using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IWhatsAppService
{
    Task SendTextMessageAsync(string to, string message);
    Task SendImageMessageAsync(string to, string imageUrl, string? caption = null);
    Task<bool> VerifyWebhookAsync(string mode, string token, string challenge);
    Task<Stream> DownloadMediaAsync(string mediaId);
}
