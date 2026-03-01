using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Fake para tests: siempre retorna éxito con URL mock.
/// CheckPaymentStatusAsync retorna no aprobado por defecto.
/// </summary>
public class FakePaymentLinkService : IPaymentLinkService
{
    /// <summary>
    /// Si true, CheckPaymentStatusAsync retorna IsApproved=true para simular pago confirmado.
    /// </summary>
    public bool SimulatePaymentApproved { get; set; }

    public Task<PaymentLinkResult> GenerateAnticipoLinkAsync(
        PaymentLinkRequest request,
        CancellationToken ct = default)
    {
        var referenceId = $"fake_{request.ConversationId:N}";
        var expiresAt = DateTime.UtcNow.AddMinutes(request.ExpirationMinutes);
        var url = $"https://checkout.example.com/pay/{referenceId}";

        return Task.FromResult(new PaymentLinkResult(
            Success: true,
            PaymentLinkUrl: url,
            PaymentReferenceId: referenceId,
            ExpiresAt: expiresAt,
            ErrorMessage: null));
    }

    public Task<PaymentStatusResult> CheckPaymentStatusAsync(
        string paymentReferenceId,
        Guid businessId,
        CancellationToken ct = default)
    {
        if (SimulatePaymentApproved)
        {
            return Task.FromResult(new PaymentStatusResult(
                IsApproved: true,
                TransactionId: $"fake_tx_{paymentReferenceId}",
                AmountInCents: 50000,
                ErrorMessage: null));
        }
        return Task.FromResult(new PaymentStatusResult(false, null, null, null));
    }

    public Task<VerifiedTransactionResult> VerifyTransactionAsync(string transactionId, Guid businessId, CancellationToken ct = default)
    {
        if (SimulatePaymentApproved)
        {
            return Task.FromResult(new VerifiedTransactionResult(
                IsApproved: true,
                TransactionId: transactionId,
                AmountInCents: 50000,
                PaymentLinkId: $"fake_{transactionId}",
                ErrorMessage: null));
        }
        return Task.FromResult(new VerifiedTransactionResult(false, null, null, null, null));
    }
}
