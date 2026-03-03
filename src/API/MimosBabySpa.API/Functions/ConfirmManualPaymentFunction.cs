using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.API.Functions;

/// <summary>
/// GET /api/confirm-payment?ptx={paymentReferenceId}&t={token}
/// El admin pulsa el link de la notificación de escalado para confirmar un pago manual.
/// Valida token HMAC y ejecuta el flujo de confirmación (crear reserva + notificar cliente).
/// </summary>
public class ConfirmManualPaymentFunction
{
    private readonly IAdminActionLinkService _adminActionLinkService;
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IPaymentConfirmationHandler _paymentHandler;
    private readonly ILogger<ConfirmManualPaymentFunction> _logger;

    public ConfirmManualPaymentFunction(
        IAdminActionLinkService adminActionLinkService,
        IPaymentTransactionRepository paymentTransactionRepository,
        IPaymentConfirmationHandler paymentHandler,
        ILogger<ConfirmManualPaymentFunction> logger)
    {
        _adminActionLinkService = adminActionLinkService;
        _paymentTransactionRepository = paymentTransactionRepository;
        _paymentHandler = paymentHandler;
        _logger = logger;
    }

    [Function("ConfirmManualPayment")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "confirm-payment")] HttpRequestData req)
    {
        var query = QueryHelpers.ParseQuery(req.Url.Query);
        if (!query.TryGetValue("ptx", out var ptxValue) || !query.TryGetValue("t", out var tokenValue))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(ErrorHtml("Parámetros inválidos. Use ptx y t."));
            bad.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return bad;
        }

        var paymentReferenceId = ptxValue!.ToString().Trim();
        if (string.IsNullOrWhiteSpace(paymentReferenceId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(ErrorHtml("PaymentReferenceId inválido."));
            bad.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return bad;
        }

        if (!_adminActionLinkService.ValidatePaymentConfirmationToken(paymentReferenceId, tokenValue!))
        {
            _logger.LogWarning("ConfirmPayment: token inválido para Ref={Ref}", paymentReferenceId);
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteStringAsync(ErrorHtml("Enlace no válido o expirado."));
            forbidden.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return forbidden;
        }

        var paymentTx = await _paymentTransactionRepository.GetByPaymentReferenceIdAsync(paymentReferenceId);
        if (paymentTx == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.NotFound);
            await bad.WriteStringAsync(ErrorHtml("Transacción no encontrada."));
            bad.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return bad;
        }

        var result = await _paymentHandler.HandleAsync(
            paymentReferenceId,
            "manual",
            paymentTx.AmountInCents,
            "[Manual confirmation by admin]");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        var html = result.Success
            ? SuccessHtml()
            : ErrorHtml(result.ErrorMessage ?? "Error al confirmar el pago.");
        await response.WriteStringAsync(html);
        return response;
    }

    private static string SuccessHtml() =>
        """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Pago confirmado</title></head><body style="font-family:sans-serif;margin:2em;text-align:center">
        <h2>✅ Pago confirmado</h2>
        <p>La reserva ha sido creada y el cliente ha sido notificado.</p>
        </body></html>
        """;

    private static string ErrorHtml(string msg) =>
        $"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Error</title></head><body style="font-family:sans-serif;margin:2em;text-align:center">
        <h2>❌ {msg}</h2>
        </body></html>
        """;
}
