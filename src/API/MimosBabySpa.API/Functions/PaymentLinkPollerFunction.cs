using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.API.Functions;

/// <summary>
/// Timer Function que cada 5 minutos verifica los payment links pendientes.
/// Llama a la API de Wompi para detectar pagos que no llegaron vía webhook.
/// </summary>
public class PaymentLinkPollerFunction
{
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentConfirmationHandler _paymentHandler;
    private readonly ILogger<PaymentLinkPollerFunction> _logger;

    /// <summary>
    /// Ventana de búsqueda: transacciones creadas en las últimas 3 horas.
    /// Links típicamente expiran en 2h; 3h cubre retrasos del webhook.
    /// </summary>
    private static readonly TimeSpan PollingWindow = TimeSpan.FromHours(3);

    public PaymentLinkPollerFunction(
        IPaymentTransactionRepository paymentTransactionRepository,
        IPaymentLinkService paymentLinkService,
        IPaymentConfirmationHandler paymentHandler,
        ILogger<PaymentLinkPollerFunction> logger)
    {
        _paymentTransactionRepository = paymentTransactionRepository;
        _paymentLinkService = paymentLinkService;
        _paymentHandler = paymentHandler;
        _logger = logger;
    }

    [Function("PaymentLinkPoller")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo,
        CancellationToken ct)
    {
        _logger.LogInformation("PaymentLinkPoller: inicio ejecución");

        var createdAfter = DateTime.UtcNow - PollingWindow;
        var pending = await _paymentTransactionRepository.GetPendingAutomatedTransactionsAsync(createdAfter, ct);

        if (pending.Count == 0)
        {
            _logger.LogDebug("PaymentLinkPoller: sin transacciones pendientes");
            return;
        }

        _logger.LogInformation("PaymentLinkPoller: verificando {Count} links pendientes", pending.Count);

        foreach (var tx in pending)
        {
            try
            {
                var status = await _paymentLinkService.CheckPaymentStatusAsync(tx.PaymentReferenceId, tx.BusinessId, ct);
                if (!status.IsApproved)
                    continue;

                _logger.LogInformation("PaymentLinkPoller: pago detectado Ref={Ref} TxId={TxId}",
                    tx.PaymentReferenceId, status.TransactionId);

                var result = await _paymentHandler.HandleAsync(
                    tx.PaymentReferenceId,
                    status.TransactionId ?? "",
                    status.AmountInCents ?? tx.AmountInCents,
                    $"[Poller {DateTime.UtcNow:o}]",
                    ct);

                if (result.Success)
                    _logger.LogInformation("PaymentLinkPoller: reserva creada Ref={Ref}", tx.PaymentReferenceId);
                else
                    _logger.LogWarning("PaymentLinkPoller: handler falló Ref={Ref} Error={Error}",
                        tx.PaymentReferenceId, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentLinkPoller: error procesando Ref={Ref}", tx.PaymentReferenceId);
            }
        }

        _logger.LogInformation("PaymentLinkPoller: fin ejecución");
    }
}
