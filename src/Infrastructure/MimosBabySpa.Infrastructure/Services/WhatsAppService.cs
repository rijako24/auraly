using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
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
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Acknowledge falló para PhoneNumberId={PhoneNumberId}, MessageId={MessageId}: Status={Status}, Response={Response}",
                    phoneNumberId, whatsAppMessageId, response.StatusCode, responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Acknowledge falló para MessageId={MessageId}", whatsAppMessageId);
        }
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
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "WhatsApp API rechazó mensaje de texto: Status={Status}, Response={Response}, To={To}, PhoneNumberId={PhoneNumberId}, MessageLength={MessageLength}",
                response.StatusCode,
                responseBody,
                to,
                credentials.PhoneNumberId,
                message?.Length ?? 0);
            throw new DomainValidationException("WhatsApp", BuildWhatsAppDeliveryErrorMessage(responseBody));
        }

        _logger.LogInformation("Mensaje enviado a {To}", to);
    }

    public async Task<string?> SendButtonMessageAsync(
        Guid businessId,
        string to,
        string message,
        IReadOnlyList<OutboundButton> buttons)
    {
        if (buttons.Count == 0)
        {
            await SendTextMessageAsync(businessId, to, message);
            return null;
        }

        var credentials = await ResolveCredentialsAsync(businessId);
        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = message },
                action = new
                {
                    buttons = buttons.Take(3).Select(button => new
                    {
                        type = "reply",
                        reply = new
                        {
                            id = Truncate(button.Id, 256),
                            title = Truncate(button.Title, 20)
                        }
                    }).ToArray()
                }
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
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "WhatsApp API rechazÃ³ mensaje con botones: Status={Status}, Response={Response}",
                response.StatusCode, responseBody);
            throw new DomainValidationException("WhatsApp", BuildWhatsAppDeliveryErrorMessage(responseBody));
        }

        _logger.LogInformation("Mensaje con botones enviado a {To}", to);
        return TryReadSentMessageId(responseBody);
    }

    public async Task<string?> SendTemplateMessageAsync(
        Guid businessId,
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<string> headerParameters,
        IReadOnlyList<string> bodyParameters,
        IReadOnlyList<OutboundButton>? buttons = null)
    {
        var credentials = await ResolveCredentialsAsync(businessId);
        var components = new List<object>();

        if (headerParameters.Count > 0)
        {
            components.Add(new
            {
                type = "header",
                parameters = headerParameters.Select(value => new
                {
                    type = "text",
                    text = value
                }).ToArray()
            });
        }

        if (bodyParameters.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = bodyParameters.Select(value => new
                {
                    type = "text",
                    text = value
                }).ToArray()
            });
        }

        if (buttons is { Count: > 0 })
        {
            var index = 0;
            foreach (var button in buttons.Take(3))
            {
                components.Add(new
                {
                    type = "button",
                    sub_type = "quick_reply",
                    index = index.ToString(),
                    parameters = new[]
                    {
                        new
                        {
                            type = "payload",
                            payload = Truncate(button.Id, 256)
                        }
                    }
                });
                index++;
            }
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components = components.ToArray()
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
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "WhatsApp API rechazo template: Status={Status}, Response={Response}, To={To}, Template={Template}",
                response.StatusCode,
                responseBody,
                to,
                templateName);
            throw new DomainValidationException("WhatsApp", BuildWhatsAppDeliveryErrorMessage(responseBody));
        }

        _logger.LogInformation("Template WhatsApp {Template} enviado a {To}", templateName, to);
        return TryReadSentMessageId(responseBody);
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
            throw new DomainValidationException("WhatsApp", BuildWhatsAppDeliveryErrorMessage(responseBody));
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

    private static string BuildWhatsAppDeliveryErrorMessage(string responseBody)
    {
        if (responseBody.Contains("131047", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("24 hour", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("outside the allowed window", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("outside the customer service window", StringComparison.OrdinalIgnoreCase))
        {
            return "WhatsApp rechazo el mensaje porque la conversacion esta fuera de la ventana de atencion de 24 horas. Para reabrirla se debe enviar una plantilla aprobada.";
        }

        if (responseBody.Contains("access token", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("OAuth", StringComparison.OrdinalIgnoreCase))
        {
            return "WhatsApp rechazo el mensaje por un problema con el token de acceso configurado.";
        }

        if (responseBody.Contains("recipient", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("phone number", StringComparison.OrdinalIgnoreCase))
        {
            return "WhatsApp rechazo el mensaje. Revisa que el numero del cliente y el numero de WhatsApp del negocio esten configurados correctamente.";
        }

        return "WhatsApp rechazo el mensaje. Revisa la configuracion del canal y vuelve a intentar.";
    }
    private async Task<WhatsAppCredentials> ResolveCredentialsAsync(Guid businessId)
    {
        var credentials = await _credentialResolver.ResolveAsync(businessId);
        if (credentials == null)
            throw new InvalidOperationException($"No hay credenciales WhatsApp configuradas para el negocio {businessId}");

        return credentials;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TryReadSentMessageId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var messages = doc.RootElement.TryGetProperty("messages", out var messagesElement)
                ? messagesElement
                : default;

            if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
                return null;

            var first = messages[0];
            return first.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}


