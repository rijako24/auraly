using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Messaging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IWhatsAppCredentialResolver _credentialResolver;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly string _verifyToken;

    public WhatsAppService(
        HttpClient httpClient,
        IWhatsAppCredentialResolver credentialResolver,
        ILogger<WhatsAppService> logger,
        IOptions<WhatsAppWebhookOptions> webhookOptions)
    {
        _httpClient = httpClient;
        _credentialResolver = credentialResolver;
        _logger = logger;

        var options = webhookOptions?.Value
            ?? throw new InvalidOperationException("WhatsApp:Webhook no está configurado.");
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            throw new InvalidOperationException("WhatsApp:Webhook:ApiBaseUrl es obligatorio.");
        if (string.IsNullOrWhiteSpace(options.VerifyToken))
            throw new InvalidOperationException("WhatsApp:Webhook:VerifyToken es obligatorio.");

        _verifyToken = options.VerifyToken;
        _httpClient.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
    }

    public async Task AcknowledgeMessageAsync(string phoneNumberId, string accessToken, string whatsAppMessageId)
    {
        if (string.IsNullOrWhiteSpace(whatsAppMessageId) || string.IsNullOrWhiteSpace(phoneNumberId))
            return;

        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                status = "read",
                message_id = whatsAppMessageId,
                typing_indicator = new { type = "text" }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{phoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Acknowledge falló para MessageId={MessageId}: {Status}",
                    whatsAppMessageId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Acknowledge falló para MessageId={MessageId}", whatsAppMessageId);
        }
    }

    public async Task SendTextMessageAsync(Guid businessId, string to, string message)
    {
        var credentials = await ResolveCredentialsAsync(businessId);
        var body = FitTextBody(to, message);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = to,
            type = "text",
            text = new { body }
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

    public async Task SendDocumentMessageAsync(Guid businessId, string to, string documentUrl, string? caption = null, string? filename = null)
    {
        var credentials = await ResolveCredentialsAsync(businessId);

        _logger.LogInformation(
            "Enviando documento a WhatsApp API: To={To}, DocumentUrlLength={UrlLength}, Filename={Filename}",
            to, documentUrl?.Length ?? 0, filename);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = to,
            type = "document",
            document = new { link = documentUrl, caption = caption ?? string.Empty, filename = filename ?? string.Empty }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{credentials.PhoneNumberId}/messages")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "WhatsApp API rechazó el documento: Status={Status}, Response={Response}",
                response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "WhatsApp API aceptó el documento (Status={Status}): To={To}, ResponseLength={Length}",
            response.StatusCode, to, responseBody?.Length ?? 0);
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
        return Task.FromResult(token == _verifyToken);
    }

    private string FitTextBody(string to, string message)
    {
        if (message.Length <= WhatsAppMessageLimits.MaxTextBodyChars)
            return message;

        _logger.LogWarning(
            "Texto WhatsApp truncado de {Original} a {Max} chars para destinatario {To}",
            message.Length,
            WhatsAppMessageLimits.MaxTextBodyChars,
            to);

        return message[..WhatsAppMessageLimits.MaxTextBodyChars].Trim();
    }

    private async Task<WhatsAppCredentials> ResolveCredentialsAsync(Guid businessId)
    {
        var credentials = await _credentialResolver.ResolveAsync(businessId);
        if (credentials == null)
            throw new InvalidOperationException($"No hay credenciales WhatsApp configuradas para el negocio {businessId}");

        return credentials;
    }
}
