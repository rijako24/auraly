using Microsoft.Extensions.Logging;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class PaymentLinkPollingProcess : ITimedProcess
{
    public const string ProcessName = "payment_link_polling";
    private static readonly TimeSpan PollingWindow = TimeSpan.FromHours(3);

    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentConfirmationHandler _paymentHandler;
    private readonly ILogger<PaymentLinkPollingProcess> _logger;

    public PaymentLinkPollingProcess(
        IPaymentTransactionRepository paymentTransactionRepository,
        IPaymentLinkService paymentLinkService,
        IPaymentConfirmationHandler paymentHandler,
        ILogger<PaymentLinkPollingProcess> logger)
    {
        _paymentTransactionRepository = paymentTransactionRepository;
        _paymentLinkService = paymentLinkService;
        _paymentHandler = paymentHandler;
        _logger = logger;
    }

    public string Name => ProcessName;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var createdAfter = DateTime.UtcNow - PollingWindow;
        var pending = await _paymentTransactionRepository.GetPendingAutomatedTransactionsAsync(createdAfter, ct);

        if (pending.Count == 0)
        {
            _logger.LogDebug("PaymentLinkPollingProcess: sin transacciones pendientes");
            return;
        }

        _logger.LogInformation("PaymentLinkPollingProcess: verificando {Count} links pendientes", pending.Count);

        foreach (var tx in pending)
        {
            try
            {
                var status = await _paymentLinkService.CheckPaymentStatusAsync(
                    tx.PaymentReferenceId,
                    tx.BusinessId,
                    ct,
                    tx.MerchantConfigurationVersion);
                if (!status.IsApproved)
                    continue;

                var result = await _paymentHandler.HandleAsync(
                    tx.PaymentReferenceId,
                    status.TransactionId ?? string.Empty,
                    status.AmountInCents ?? tx.AmountInCents,
                    $"[Poller {DateTime.UtcNow:o}]",
                    ct,
                    PaymentTransactionSource.Automated);

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "PaymentLinkPollingProcess: handler fallo Ref={Ref} Error={Error}",
                        tx.PaymentReferenceId,
                        result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentLinkPollingProcess: error procesando Ref={Ref}", tx.PaymentReferenceId);
            }
        }
    }
}
