using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Infrastructure.Services;

public class WhatsAppWebhookOptions
{
    public const string SectionName = "WhatsApp:Webhook";
    public string VerifyToken { get; set; } = string.Empty;
}

public class WhatsAppService : IWhatsAppService
{
    private const string ApiBaseUrl = "https://graph.facebook.com/v22.0/";

    private readonly HttpClient _httpClient;
    private readonly IWhatsAppCredentialResolver _credentialResolver;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly string? _verifyToken;

    public WhatsAppService(
        HttpClient httpClient,
        IWhatsAppCredentialResolver credentialResolver,
        ILogger<WhatsAppService> logger,
        IOptions<WhatsAppWebhookOptions>? webhookOptions = null)
    {
        _httpClient = httpClient;
        _credentialResolver = credentialResolver;
        _logger = logger;
        _verifyToken = webhookOptions?.Value?.VerifyToken;
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
    }

    public async Task SendTextMessageAsync(Guid businessId, string to, string message)
    {
        var credentials = await ResolveCredentialsAsync(businessId);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = to,
            type = "text",
            text = new { body = message }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{credentials.PhoneNumberId}/messages")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Mensaje enviado a {To}", to);
    }

    public async Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null)
    {
        var credentials = await ResolveCredentialsAsync(businessId);

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

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{credentials.PhoneNumberId}/messages")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Imagen enviada a {To}", to);
    }

    public async Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId)
    {
        var credentials = await ResolveCredentialsAsync(businessId);

        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, mediaId);
        metadataRequest.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        var metadataResponse = await _httpClient.SendAsync(metadataRequest);
        metadataResponse.EnsureSuccessStatusCode();

        var jsonResponse = await metadataResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResponse);

        var url = doc.RootElement.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("No se pudo obtener la URL del media");

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, url);
        downloadRequest.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        var mediaResponse = await _httpClient.SendAsync(downloadRequest);
        mediaResponse.EnsureSuccessStatusCode();

        var stream = new MemoryStream();
        await mediaResponse.Content.CopyToAsync(stream);
        stream.Position = 0;

        _logger.LogInformation("Media descargado: {MediaId}", mediaId);
        return stream;
    }

    public Task<bool> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(challenge))
            return Task.FromResult(false);
        if (!string.IsNullOrEmpty(_verifyToken))
            return Task.FromResult(token == _verifyToken);
        return Task.FromResult(true);
    }

    private async Task<WhatsAppCredentials> ResolveCredentialsAsync(Guid businessId)
    {
        var credentials = await _credentialResolver.ResolveAsync(businessId);
        if (credentials == null)
            throw new InvalidOperationException($"No hay credenciales WhatsApp configuradas para el negocio {businessId}");

        return credentials;
    }
}
