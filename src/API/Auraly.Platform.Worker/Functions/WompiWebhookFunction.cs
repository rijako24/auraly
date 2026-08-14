using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Worker.Functions;

/// <summary>
/// Webhook para recibir eventos de pago de Wompi.
/// POST /api/WompiWebhook?b={businessId}
/// La URL del webhook debe incluir el businessId para validar firma y verificar transacción por negocio.
/// Valida la firma, extrae transaction_id y verifica en la API de Wompi antes de procesar.
/// </summary>
public class WompiWebhookFunction
{
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentConfirmationHandler _paymentHandler;
    private readonly IWompiWebhookSignatureValidator _signatureValidator;
    private readonly IIntegrationsConfigProvider _integrationsProvider;
    private readonly ILogger<WompiWebhookFunction> _logger;

    public WompiWebhookFunction(
        IPaymentLinkService paymentLinkService,
        IPaymentConfirmationHandler paymentHandler,
        IWompiWebhookSignatureValidator signatureValidator,
        IIntegrationsConfigProvider integrationsProvider,
        ILogger<WompiWebhookFunction> logger)
    {
        _paymentLinkService = paymentLinkService;
        _paymentHandler = paymentHandler;
        _signatureValidator = signatureValidator;
        _integrationsProvider = integrationsProvider;
        _logger = logger;
    }

    [Function("WompiWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        try
        {
            var query = QueryHelpers.ParseQuery(req.Url.Query);
            if (!query.TryGetValue("b", out var businessIdStr) || !Guid.TryParse(businessIdStr, out var businessId))
            {
                _logger.LogWarning("Webhook Wompi: falta parámetro b (businessId) en la URL. Use /api/WompiWebhook?b={{businessId}}");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var integrations = await _integrationsProvider.GetAsync(businessId);
            var wompi = integrations?.Wompi;
            if (wompi == null || string.IsNullOrWhiteSpace(wompi.EventsSecret))
            {
                _logger.LogWarning("Webhook Wompi: Wompi no configurado o EventsSecret vacío para BusinessId={BusinessId}", businessId);
                return req.CreateResponse(HttpStatusCode.Unauthorized);
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            if (!_signatureValidator.Validate(root, wompi.EventsSecret))
            {
                _logger.LogWarning("Webhook Wompi: firma inválida o checksum no coincide");
                return req.CreateResponse(HttpStatusCode.Unauthorized);
            }

            var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
            if (eventType != "transaction.updated")
            {
                _logger.LogDebug("Webhook Wompi: evento ignorado {Event}", eventType);
                return req.CreateResponse(HttpStatusCode.OK);
            }

            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("transaction", out var tx))
            {
                _logger.LogWarning("Webhook Wompi: estructura inválida (falta data.transaction)");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var status = tx.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
            if (status != "APPROVED")
            {
                _logger.LogDebug("Webhook Wompi: transacción no aprobada Status={Status}", status);
                return req.CreateResponse(HttpStatusCode.OK);
            }

            var transactionId = tx.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                _logger.LogWarning("Webhook Wompi: sin transaction id");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var verified = await _paymentLinkService.VerifyTransactionAsync(transactionId, businessId, CancellationToken.None);
            if (!verified.IsApproved)
            {
                _logger.LogWarning("Webhook Wompi: verificación API falló TxId={TxId} Error={Error}", transactionId, verified.ErrorMessage);
                return req.CreateResponse(HttpStatusCode.OK);
            }

            var paymentReferenceId = verified.PaymentLinkId;
            if (string.IsNullOrWhiteSpace(paymentReferenceId))
            {
                _logger.LogWarning("Webhook Wompi: transacción verificada sin payment_link_id TxId={TxId}", transactionId);
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var amountInCents = verified.AmountInCents ?? 0L;

            var result = await _paymentHandler.HandleAsync(
                paymentReferenceId,
                transactionId,
                amountInCents,
                requestBody,
                CancellationToken.None);

            if (!result.Success)
            {
                _logger.LogWarning("Webhook Wompi: handler falló Ref={Ref} TxId={TxId} Error={Error}",
                    paymentReferenceId, transactionId, result.ErrorMessage);
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Webhook Wompi: JSON inválido");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook Wompi: error inesperado");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}
