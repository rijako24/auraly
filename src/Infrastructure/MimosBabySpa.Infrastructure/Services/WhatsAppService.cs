using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Infrastructure.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        HttpClient httpClient,
        string phoneNumberId,
        string accessToken,
        ILogger<WhatsAppService> logger)
    {
        _httpClient = httpClient;
        _phoneNumberId = phoneNumberId;
        _accessToken = accessToken;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri("https://graph.facebook.com/v18.0/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
    }

    public async Task SendTextMessageAsync(string to, string message)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to = to,
                type = "text",
                text = new { body = message }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_phoneNumberId}/messages", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Mensaje enviado a {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar mensaje a {To}", to);
            throw;
        }
    }

    public async Task SendImageMessageAsync(string to, string imageUrl, string? caption = null)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to = to,
                type = "image",
                image = new
                {
                    link = imageUrl,
                    caption = caption ?? string.Empty
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_phoneNumberId}/messages", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Imagen enviada a {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar imagen a {To}", to);
            throw;
        }
    }

    public async Task<Stream> DownloadMediaAsync(string mediaId)
    {
        try
        {
            // Obtener la URL del media usando la API de WhatsApp
            // La URL es: https://graph.facebook.com/v18.0/{media-id}
            var response = await _httpClient.GetAsync(mediaId);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
            
            var url = doc.RootElement.GetProperty("url").GetString();
            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException("No se pudo obtener la URL del media");
            }

            // Descargar el archivo usando la URL obtenida
            // Necesitamos usar un HttpClient sin el BaseAddress para la descarga
            using var downloadClient = new HttpClient();
            downloadClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
            
            var mediaResponse = await downloadClient.GetAsync(url);
            mediaResponse.EnsureSuccessStatusCode();

            var stream = new MemoryStream();
            await mediaResponse.Content.CopyToAsync(stream);
            stream.Position = 0;

            _logger.LogInformation("Media descargado: {MediaId}", mediaId);
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar media {MediaId}", mediaId);
            throw;
        }
    }

    public Task<bool> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        // Implementar verificación del webhook según la configuración
        // Por ahora, retornamos true si mode == "subscribe"
        var isValid = mode == "subscribe" && !string.IsNullOrEmpty(challenge);
        return Task.FromResult(isValid);
    }
}
