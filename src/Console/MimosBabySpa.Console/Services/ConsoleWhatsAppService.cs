using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Agents;

namespace MimosBabySpa.Console.Services;

/// <summary>
/// Implementación mock de IWhatsAppService para la aplicación de consola.
/// En lugar de enviar mensajes por WhatsApp, los muestra en la consola.
/// </summary>
public class ConsoleWhatsAppService : IWhatsAppService
{
    private readonly ILogger<ConsoleWhatsAppService> _logger;

    public ConsoleWhatsAppService(ILogger<ConsoleWhatsAppService> logger)
    {
        _logger = logger;
    }

    public Task AcknowledgeMessageAsync(string phoneNumberId, string accessToken, string whatsAppMessageId)
    {
        return Task.CompletedTask;
    }

    public Task SendTextMessageAsync(Guid businessId, string to, string message)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("🤖 Bot:");
        System.Console.WriteLine(message);
        System.Console.WriteLine();
        return Task.CompletedTask;
    }

    public Task<string?> SendButtonMessageAsync(Guid businessId, string to, string message, IReadOnlyList<OutboundButton> buttons)
    {
        _logger.LogInformation("WhatsApp botones mock a {To}: {Message} | {Buttons}",
            to,
            message,
            string.Join(", ", buttons.Select(b => $"{b.Title}:{b.Id}")));
        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }

    public Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("🤖 Bot (imagen):");
        if (!string.IsNullOrEmpty(caption))
        {
            System.Console.WriteLine(caption);
        }
        System.Console.WriteLine($"📷 URL de imagen: {imageUrl}");
        System.Console.WriteLine();
        return Task.CompletedTask;
    }

    public Task SendDocumentMessageAsync(Guid businessId, string to, string documentUrl, string? caption = null, string? filename = null)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("🤖 Bot (documento):");
        if (!string.IsNullOrEmpty(caption))
        {
            System.Console.WriteLine(caption);
        }
        System.Console.WriteLine($"📄 URL de documento: {documentUrl}");
        if (!string.IsNullOrEmpty(filename))
        {
            System.Console.WriteLine($"   Nombre: {filename}");
        }
        System.Console.WriteLine();
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId)
    {
        // En la aplicación de consola, no podemos descargar media real de WhatsApp
        // Retornamos un stream vacío o lanzamos excepción según el caso de uso
        System.Console.WriteLine($"⚠️  Intento de descargar media {mediaId} - No soportado en modo consola");
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<bool> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        // Para la consola, siempre retornamos true
        return Task.FromResult(true);
    }
}
