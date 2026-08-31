using Auraly.Contracts.TenantBilling;

namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record StartTenantSubscriptionCheckoutRequest(string? RedirectUrl = null);

public sealed record StartTenantSubscriptionCheckoutResult(
    Guid RenewalOrderId,
    TenantProvisioningWidgetDto Widget);

public sealed record ConfirmTenantSubscriptionPaymentRequest(string TransactionId);

public sealed record RecordTenantSubscriptionPaymentRequest(
    string PaymentMethodCode,
    string Reference,
    DateTimeOffset PaidAt,
    string? Note);

public sealed record TenantSubscriptionManualPaymentPreparation(
    string PaymentReference,
    long AmountInCents,
    string ExternalReference);

public sealed record TenantSubscriptionPaymentVerification(
    Guid RenewalOrderId,
    Guid PaymentTransactionId,
    Guid BillingBusinessId,
    string PaymentReference,
    long AmountInCents,
    DateTimeOffset ExpiresAt,
    int PaymentStatus,
    int MerchantConfigurationVersion);

public sealed record TenantSubscriptionReceiptLineDto(
    string Code,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetAmount,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount);

public sealed record TenantSubscriptionReceiptDto(
    Guid DocumentId,
    string DocumentNumber,
    DateTimeOffset IssuedAt,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string BillingPeriod,
    string CurrencyCode,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string PaymentMethod,
    string PaymentReference,
    string? Cufe,
    string FiscalStatus,
    IReadOnlyList<TenantSubscriptionReceiptLineDto> Lines);

public interface ITenantSubscriptionCheckoutStore
{
    Task<Guid> GetBillingBusinessIdAsync(CancellationToken cancellationToken);
    Task CreatePaymentAsync(
        Guid tenantId,
        Guid paymentTransactionId,
        Guid renewalOrderId,
        string reference,
        long amountInCents,
        DateTimeOffset expiresAt,
        int merchantConfigurationVersion,
        CancellationToken cancellationToken);
    Task<TenantSubscriptionPaymentVerification?> GetPaymentForVerificationAsync(
        Guid tenantId,
        Guid renewalOrderId,
        CancellationToken cancellationToken);
    Task<TenantSubscriptionManualPaymentPreparation> CreateManualPaymentAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid paymentTransactionId,
        Guid renewalOrderId,
        RecordTenantSubscriptionPaymentRequest request,
        string checkoutSnapshotJson,
        CancellationToken cancellationToken);
    Task<TenantSubscriptionReceiptDto?> GetReceiptAsync(
        Guid tenantId,
        Guid renewalOrderId,
        CancellationToken cancellationToken);
}
