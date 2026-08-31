using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Infrastructure.Configuration;

namespace Auraly.Platform.Infrastructure.Services;

/// <summary>
/// Implementación de IPaymentLinkService usando Wompi API.
/// Configuración por negocio vía IIntegrationsConfigProvider.
/// Si PrivateKey no está configurado, no genera link (Success: false).
/// </summary>
public class WompiPaymentLinkService : IPaymentLinkService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIntegrationsConfigProvider _integrationsProvider;
    private readonly ILogger<WompiPaymentLinkService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public WompiPaymentLinkService(
        IHttpClientFactory httpClientFactory,
        IIntegrationsConfigProvider integrationsProvider,
        ILogger<WompiPaymentLinkService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _integrationsProvider = integrationsProvider;
        _logger = logger;
    }

    public async Task<PaymentLinkResult> GenerateAnticipoLinkAsync(
        PaymentLinkRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var wompi = await _integrationsProvider.GetWompiAsync(request.BusinessId, cancellationToken: ct);
        if (wompi == null || string.IsNullOrWhiteSpace(wompi.PrivateKey))
        {
            _logger.LogWarning("Link de pago solicitado pero Wompi no configurado (PrivateKey vacío) para BusinessId={BusinessId}", request.BusinessId);
            return new PaymentLinkResult(
                Success: false,
                PaymentLinkUrl: null,
                PaymentReferenceId: null,
                ExpiresAt: null,
                ErrorMessage: "Pagos no configurados. Configure la integración con Wompi.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(request.ExpirationMinutes);
        return await CreatePaymentLinkAsync(request, wompi, expiresAt, ct).ConfigureAwait(false);
    }

    public async Task<WompiWidgetCheckoutResult> PrepareWidgetCheckoutAsync(
        WompiWidgetCheckoutRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Reference) || request.AmountInCents <= 0)
            return WidgetFailure("La referencia y el valor del pago son obligatorios.");

        var currency = request.Currency.Trim().ToUpperInvariant();
        if (!string.Equals(currency, "COP", StringComparison.Ordinal))
            return WidgetFailure("El checkout de Wompi solo admite COP.");

        var wompi = await _integrationsProvider.GetWompiAsync(request.BusinessId, cancellationToken: ct);
        if (wompi is null
            || string.IsNullOrWhiteSpace(wompi.PublicKey)
            || string.IsNullOrWhiteSpace(wompi.IntegritySecret))
        {
            _logger.LogWarning(
                "Widget Wompi no configurado para BusinessId={BusinessId}: faltan PublicKey o IntegritySecret",
                request.BusinessId);
            return WidgetFailure("Pagos no configurados. Configure la clave publica y el secreto de integridad de Wompi.");
        }

        var expiration = request.ExpiresAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var integrityInput = string.Concat(
            request.Reference.Trim(),
            request.AmountInCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            currency,
            expiration,
            wompi.IntegritySecret);
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(integrityInput)))
            .ToLowerInvariant();

        return new WompiWidgetCheckoutResult(
            true,
            wompi.PublicKey,
            request.Reference.Trim(),
            request.AmountInCents,
            currency,
            signature,
            expiration,
            request.RedirectUrl,
            null,
            wompi.ConfigurationVersion);
    }

    private static WompiWidgetCheckoutResult WidgetFailure(string error) =>
        new(false, null, null, null, null, null, null, null, error);

    private async Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentLinkRequest request,
        WompiIntegration wompi,
        DateTime expiresAt,
        CancellationToken ct)
    {
        var baseUrl = wompi.GetBaseUrl();
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {wompi.PrivateKey}");
        client.Timeout = TimeSpan.FromSeconds(wompi.RequestTimeoutSeconds);

        var body = new WompiPaymentLinkRequest
        {
            Name = Truncate($"Anticipo reserva - {request.ServiceDescription}", 80),
            Description = request.ServiceDescription,
            SingleUse = true,
            CollectShipping = false,
            Currency = request.Currency,
            AmountInCents = request.AmountInCents,
            ExpiresAt = expiresAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Sku = request.ConversationId.ToString("N")
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync("payment_links", content, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Wompi API error: Status={Status}, Response={Response}",
                    response.StatusCode, responseBody);
                return new PaymentLinkResult(
                    Success: false,
                    PaymentLinkUrl: null,
                    PaymentReferenceId: null,
                    ExpiresAt: null,
                    ErrorMessage: $"Wompi API: {(int)response.StatusCode} - {Truncate(responseBody, 200)}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var idEl))
            {
                _logger.LogError("Wompi API: respuesta sin data.id");
                return new PaymentLinkResult(
                    Success: false,
                    PaymentLinkUrl: null,
                    PaymentReferenceId: null,
                    ExpiresAt: null,
                    ErrorMessage: "Respuesta de Wompi inválida (falta data.id)");
            }

            var paymentLinkId = idEl.GetString();
            if (string.IsNullOrWhiteSpace(paymentLinkId))
            {
                return new PaymentLinkResult(
                    Success: false,
                    PaymentLinkUrl: null,
                    PaymentReferenceId: null,
                    ExpiresAt: null,
                    ErrorMessage: "Wompi devolvió id vacío");
            }

            var checkoutBase = wompi.CheckoutBaseUrl?.TrimEnd('/') ?? "https://checkout.wompi.co/l";
            var paymentUrl = $"{checkoutBase}/{paymentLinkId}";

            _logger.LogInformation(
                "Link de pago generado: LinkId={LinkId}, BusinessId={BusinessId}, ConvId={ConvId}",
                paymentLinkId, request.BusinessId, request.ConversationId);

            return new PaymentLinkResult(
                Success: true,
                PaymentLinkUrl: paymentUrl,
                PaymentReferenceId: paymentLinkId,
                ExpiresAt: expiresAt,
                ErrorMessage: null,
                MerchantConfigurationVersion: wompi.ConfigurationVersion);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de red al llamar Wompi API");
            return new PaymentLinkResult(
                Success: false,
                PaymentLinkUrl: null,
                PaymentReferenceId: null,
                ExpiresAt: null,
                ErrorMessage: "Error de conexión con Wompi. Intenta más tarde.");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout al llamar Wompi API");
            return new PaymentLinkResult(
                Success: false,
                PaymentLinkUrl: null,
                PaymentReferenceId: null,
                ExpiresAt: null,
                ErrorMessage: "Tiempo de espera agotado. Intenta más tarde.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Respuesta JSON inválida de Wompi");
            return new PaymentLinkResult(
                Success: false,
                PaymentLinkUrl: null,
                PaymentReferenceId: null,
                ExpiresAt: null,
                ErrorMessage: "Error procesando respuesta de Wompi.");
        }
    }

    /// <inheritdoc />
    public async Task<PaymentStatusResult> CheckPaymentStatusAsync(
        string paymentReferenceId,
        Guid businessId,
        CancellationToken ct = default,
        int? merchantConfigurationVersion = null)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(paymentReferenceId))
            return new PaymentStatusResult(false, null, null, "PaymentReferenceId vacío");

        var wompi = await _integrationsProvider.GetWompiAsync(
            businessId, merchantConfigurationVersion, ct);
        if (wompi == null || string.IsNullOrWhiteSpace(wompi.PrivateKey))
        {
            _logger.LogDebug("CheckPaymentStatus: Wompi no configurado para BusinessId={BusinessId}", businessId);
            return new PaymentStatusResult(false, null, null, "Pagos no configurados");
        }

        var baseUrl = wompi.GetBaseUrl();
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {wompi.PrivateKey}");
        client.Timeout = TimeSpan.FromSeconds(wompi.RequestTimeoutSeconds);

        try
        {
            var fromDate = DateTime.UtcNow.AddDays(-15).ToString("yyyy-MM-dd");
            var untilDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var query = $"transactions?from_date={fromDate}&until_date={untilDate}&page=1&page_size=50";
            var response = await client.GetAsync(query, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("CheckPaymentStatus: link no encontrado Ref={Ref}", paymentReferenceId);
                    return new PaymentStatusResult(false, null, null, null);
                }
                _logger.LogWarning(
                    "CheckPaymentStatus: Wompi API error Status={Status} Ref={Ref}",
                    response.StatusCode, paymentReferenceId);
                return new PaymentStatusResult(false, null, null, $"Wompi: {(int)response.StatusCode}");
            }

            return ParseTransactionsListResponse(responseBody, paymentReferenceId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "CheckPaymentStatus: error de red Ref={Ref}", paymentReferenceId);
            return new PaymentStatusResult(false, null, null, "Error de conexión");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "CheckPaymentStatus: timeout Ref={Ref}", paymentReferenceId);
            return new PaymentStatusResult(false, null, null, "Timeout");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "CheckPaymentStatus: JSON inválido Ref={Ref}", paymentReferenceId);
            return new PaymentStatusResult(false, null, null, "Respuesta inválida");
        }
    }

    /// <inheritdoc />
    public async Task<VerifiedTransactionResult> VerifyTransactionAsync(
        string transactionId,
        Guid businessId,
        CancellationToken ct = default,
        int? merchantConfigurationVersion = null)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(transactionId))
            return new VerifiedTransactionResult(false, null, null, null, null, "TransactionId vacío");

        var wompi = await _integrationsProvider.GetWompiAsync(
            businessId, merchantConfigurationVersion, ct);
        if (wompi == null || string.IsNullOrWhiteSpace(wompi.PrivateKey))
        {
            _logger.LogDebug("VerifyTransaction: Wompi no configurado para BusinessId={BusinessId}", businessId);
            return new VerifiedTransactionResult(false, null, null, null, null, "Pagos no configurados");
        }

        var baseUrl = wompi.GetBaseUrl();
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {wompi.PrivateKey}");
        client.Timeout = TimeSpan.FromSeconds(wompi.RequestTimeoutSeconds);

        try
        {
            var response = await client.GetAsync($"transactions/{Uri.EscapeDataString(transactionId)}", ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("VerifyTransaction: transacción no encontrada TxId={TxId}", transactionId);
                    return new VerifiedTransactionResult(false, null, null, null, null, null);
                }
                _logger.LogWarning("VerifyTransaction: Wompi API error Status={Status} TxId={TxId}", response.StatusCode, transactionId);
                return new VerifiedTransactionResult(false, null, null, null, null, $"Wompi: {(int)response.StatusCode}");
            }

            return ParseVerifiedTransactionResponse(responseBody, transactionId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "VerifyTransaction: error de red TxId={TxId}", transactionId);
            return new VerifiedTransactionResult(false, null, null, null, null, "Error de conexión");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "VerifyTransaction: timeout TxId={TxId}", transactionId);
            return new VerifiedTransactionResult(false, null, null, null, null, "Timeout");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "VerifyTransaction: JSON inválido TxId={TxId}", transactionId);
            return new VerifiedTransactionResult(false, null, null, null, null, "Respuesta inválida");
        }
    }

    private static PaymentStatusResult ParseTransactionsListResponse(string responseBody, string paymentReferenceId)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            if (data.ValueKind != JsonValueKind.Array)
                return new PaymentStatusResult(false, null, null, null);

            foreach (var tx in data.EnumerateArray())
            {
                var txLinkId = tx.TryGetProperty("payment_link_id", out var pl) ? pl.GetString() : null;
                var txReference = tx.TryGetProperty("reference", out var referenceElement)
                    ? referenceElement.GetString()
                    : null;
                if (!string.Equals(txLinkId, paymentReferenceId, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(txReference, paymentReferenceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var (approved, txId, amount) = ParseTransactionStatus(tx);
                if (approved)
                    return new PaymentStatusResult(true, txId, amount, null);
            }
        }
        catch (JsonException) { }

        return new PaymentStatusResult(false, null, null, null);
    }

    private static VerifiedTransactionResult ParseVerifiedTransactionResponse(string responseBody, string transactionId)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var d) ? d : root;

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var isApproved = string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase);
        var txId = data.TryGetProperty("id", out var id) ? id.GetString() : transactionId;
        var amount = data.TryGetProperty("amount_in_cents", out var amt) ? amt.GetInt64() : (long?)null;
        var paymentLinkId = data.TryGetProperty("payment_link_id", out var pl) ? pl.GetString() : null;
        var reference = data.TryGetProperty("reference", out var referenceElement) ? referenceElement.GetString() : null;

        return new VerifiedTransactionResult(isApproved, txId, amount, paymentLinkId, reference, null);
    }

    private static (bool Approved, string? TxId, long? Amount) ParseTransactionStatus(JsonElement tx)
    {
        var status = tx.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return (false, null, null);
        var txId = tx.TryGetProperty("id", out var id) ? id.GetString() : null;
        var amount = tx.TryGetProperty("amount_in_cents", out var amt) ? amt.GetInt64() : (long?)null;
        return (true, txId, amount);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class WompiPaymentLinkRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("single_use")]
        public bool SingleUse { get; set; }

        [JsonPropertyName("collect_shipping")]
        public bool CollectShipping { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "COP";

        [JsonPropertyName("amount_in_cents")]
        public long AmountInCents { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }

        [JsonPropertyName("sku")]
        public string? Sku { get; set; }
    }
}
