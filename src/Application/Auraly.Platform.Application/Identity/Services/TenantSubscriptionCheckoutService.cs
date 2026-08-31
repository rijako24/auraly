using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Enums;
using System.Text.Json;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantSubscriptionCheckoutService(
    TenantRenewalOrderService renewalOrders,
    ITenantSubscriptionCheckoutStore store,
    IPaymentLinkService payments,
    IPaymentConfirmationHandler confirmation)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);
    private static readonly HashSet<string> ManualPaymentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "Cash", "Transfer", "DebitCard", "CreditCard" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StartTenantSubscriptionCheckoutResult> StartAsync(
        Guid tenantId,
        StartTenantSubscriptionCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var order = await renewalOrders.GetCurrentAsync(tenantId, cancellationToken)
            ?? throw new ArgumentException("Primero prepara la orden de renovación.");
        if (!string.Equals(order.Status, "Draft", StringComparison.Ordinal) &&
            !string.Equals(order.Status, "PendingPayment", StringComparison.Ordinal))
            throw new ArgumentException("La orden vigente no está disponible para iniciar un pago.");

        var billingBusinessId = await store.GetBillingBusinessIdAsync(cancellationToken);
        var amountInCents = checked((long)decimal.Round(
            order.Quote.PayableAmountCop * 100m, 0, MidpointRounding.AwayFromZero));
        var reference = $"TS-{order.RenewalOrderId:N}";
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var mustCreatePayment = string.Equals(order.Status, "Draft", StringComparison.Ordinal);

        if (!mustCreatePayment)
        {
            var existing = await store.GetPaymentForVerificationAsync(
                tenantId, order.RenewalOrderId, cancellationToken)
                ?? throw new InvalidOperationException("La orden pendiente no tiene un pago asociado.");
            if (existing.PaymentStatus != (int)PaymentTransactionStatus.Created ||
                existing.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException(
                    "El intento de pago pendiente ya no se puede retomar. Espera su conciliación o contacta soporte.");
            if (existing.BillingBusinessId != billingBusinessId ||
                existing.AmountInCents != amountInCents ||
                !string.Equals(existing.PaymentReference, reference, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "El pago pendiente no coincide con la orden vigente.");
            expiresAt = existing.ExpiresAt;
        }

        var widget = await payments.PrepareWidgetCheckoutAsync(new(
            billingBusinessId, reference, amountInCents, "COP", expiresAt,
            request.RedirectUrl), cancellationToken);
        if (!widget.Success || string.IsNullOrWhiteSpace(widget.PublicKey) ||
            string.IsNullOrWhiteSpace(widget.IntegritySignature))
            throw new InvalidOperationException(
                widget.ErrorMessage ?? "No fue posible preparar el pago con Wompi.");

        if (mustCreatePayment)
            await store.CreatePaymentAsync(tenantId, Guid.NewGuid(),
                order.RenewalOrderId, reference, amountInCents, expiresAt,
                widget.MerchantConfigurationVersion, cancellationToken);
        return new(order.RenewalOrderId, new(
            widget.PublicKey!, reference, amountInCents, "COP",
            widget.IntegritySignature!, widget.ExpirationTime, widget.RedirectUrl));
    }

    public async Task<TenantSubscriptionReceiptDto> ConfirmAsync(
        Guid tenantId,
        Guid renewalOrderId,
        ConfirmTenantSubscriptionPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
            throw new ArgumentException("La transacción de Wompi es obligatoria.");
        var expected = await store.GetPaymentForVerificationAsync(
            tenantId, renewalOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("No se encontró el pago de esta renovación.");
        var verified = await payments.VerifyTransactionAsync(
            request.TransactionId.Trim(), expected.BillingBusinessId,
            cancellationToken, expected.MerchantConfigurationVersion);
        if (!verified.IsApproved ||
            !string.Equals(verified.Reference, expected.PaymentReference,
                StringComparison.Ordinal) ||
            verified.AmountInCents != expected.AmountInCents)
            throw new InvalidOperationException(
                "Wompi todavía no confirma este pago con el valor y la referencia esperados.");

        var result = await confirmation.HandleAsync(
            expected.PaymentReference,
            verified.TransactionId ?? request.TransactionId.Trim(),
            expected.AmountInCents,
            $"[Widget verification {DateTimeOffset.UtcNow:O}]",
            cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(
                result.ErrorMessage ?? "No fue posible confirmar la renovación.");
        return await GetReceiptAsync(tenantId, renewalOrderId, cancellationToken);
    }

    public async Task<TenantSubscriptionReceiptDto> RecordManualPaymentAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid renewalOrderId,
        RecordTenantSubscriptionPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = request.PaymentMethodCode?.Trim() ?? string.Empty;
        var reference = request.Reference?.Trim() ?? string.Empty;
        var note = request.Note?.Trim();
        var now = DateTimeOffset.UtcNow;
        if (!ManualPaymentMethods.Contains(method))
            throw new ArgumentException("Selecciona efectivo, transferencia, tarjeta débito o tarjeta crédito.");
        if (reference.Length is < 3 or > 160)
            throw new ArgumentException("La referencia del recaudo debe tener entre 3 y 160 caracteres.");
        if (request.PaidAt > now.AddMinutes(5) || request.PaidAt < now.AddYears(-2))
            throw new ArgumentException("La fecha del recaudo no es válida.");
        if (note?.Length > 500)
            throw new ArgumentException("La observación no puede superar 500 caracteres.");

        var paymentId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new
        {
            paymentMethodCode = CanonicalMethod(method),
            externalReference = reference,
            paidAt = request.PaidAt,
            note,
            recordedByUserId = actorUserId,
            recordedAt = now
        }, Json);
        var prepared = await store.CreateManualPaymentAsync(
            tenantId, actorUserId, paymentId, renewalOrderId,
            request with { PaymentMethodCode = CanonicalMethod(method), Reference = reference, Note = note },
            snapshot, cancellationToken);
        var result = await confirmation.HandleAsync(
            prepared.PaymentReference,
            prepared.ExternalReference,
            prepared.AmountInCents,
            snapshot,
            cancellationToken,
            PaymentTransactionSource.Manual);
        if (!result.Success)
            throw new InvalidOperationException(
                result.ErrorMessage ?? "No fue posible aplicar el recaudo externo.");
        return await GetReceiptAsync(tenantId, renewalOrderId, cancellationToken);
    }

    public async Task<TenantSubscriptionReceiptDto> GetReceiptAsync(
        Guid tenantId, Guid renewalOrderId, CancellationToken cancellationToken) =>
        await store.GetReceiptAsync(tenantId, renewalOrderId, cancellationToken)
        ?? throw new KeyNotFoundException("La factura de esta renovación aún no está disponible.");

    private static string CanonicalMethod(string value) =>
        ManualPaymentMethods.Single(method => method.Equals(value, StringComparison.OrdinalIgnoreCase));
}
