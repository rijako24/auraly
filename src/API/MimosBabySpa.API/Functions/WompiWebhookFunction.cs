using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.API.Functions;

/// <summary>
/// Webhook para recibir eventos de pago de Wompi.
/// POST /api/WompiWebhook
/// Valida la firma, extrae transaction_id y verifica en la API de Wompi antes de procesar.
/// No confía en el payload: verificación independiente con GET /transactions/{id}.
/// </summary>
public class WompiWebhookFunction
{
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentConfirmationHandler _paymentHandler;
    private readonly IWompiWebhookSignatureValidator _signatureValidator;
    private readonly WompiSettings _wompiSettings;
    private readonly ILogger<WompiWebhookFunction> _logger;

    public WompiWebhookFunction(
        IPaymentLinkService paymentLinkService,
        IPaymentConfirmationHandler paymentHandler,
        IWompiWebhookSignatureValidator signatureValidator,
        IOptions<WompiSettings> wompiSettings,
        ILogger<WompiWebhookFunction> logger)
    {
        _paymentLinkService = paymentLinkService;
        _paymentHandler = paymentHandler;
        _signatureValidator = signatureValidator;
        _wompiSettings = wompiSettings.Value;
        _logger = logger;
    }

    [Function("WompiWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            if (!_signatureValidator.Validate(root, _wompiSettings.EventsSecret))
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

            var verified = await _paymentLinkService.VerifyTransactionAsync(transactionId, CancellationToken.None);
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
