using Auraly.Contracts.TenantBilling;
using Auraly.Contracts.Tenants;

namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record StartTenantProvisioningCheckoutRequest(
    ProvisionTenantRequest Tenant,
    TenantQuoteRequest Quote,
    string? RedirectUrl = null);

public sealed record TenantProvisioningWidgetDto(
    string PublicKey,
    string Reference,
    long AmountInCents,
    string Currency,
    string IntegritySignature,
    string? ExpirationTime,
    string? RedirectUrl);

public sealed record StartTenantProvisioningCheckoutResult(
    Guid DraftId,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    TenantQuoteDto Quote,
    TenantProvisioningWidgetDto Widget);

public sealed record TenantProvisioningCheckoutStatusDto(
    Guid DraftId,
    string Status,
    string PaymentStatus,
    Guid? TenantId,
    string? TenantKey,
    string? ErrorMessage);

public sealed record TenantProvisioningCheckoutSnapshot(
    ProvisionTenantRequest Tenant,
    TenantQuoteDto Quote);

public sealed record TenantProvisioningFulfillment(
    Guid DraftId,
    Guid PaymentTransactionId,
    TenantProvisioningCheckoutSnapshot Snapshot,
    string Status);

public sealed record ConfirmTenantProvisioningWidgetPaymentRequest(string TransactionId);

public sealed record TenantProvisioningPaymentVerification(
    Guid DraftId,
    Guid PaymentTransactionId,
    Guid BillingBusinessId,
    string PaymentReference,
    long AmountInCents,
    int MerchantConfigurationVersion);

public interface ITenantProvisioningCheckoutStore
{
    Task<Guid> GetBillingBusinessIdAsync(CancellationToken cancellationToken);
    Task CreateAsync(
        Guid draftId,
        Guid paymentTransactionId,
        byte[] accessTokenHash,
        string ownerEmail,
        TenantProvisioningCheckoutSnapshot snapshot,
        byte[] quoteHash,
        DateTimeOffset expiresAt,
        int merchantConfigurationVersion,
        CancellationToken cancellationToken);
    Task<TenantProvisioningCheckoutStatusDto?> GetStatusAsync(
        Guid draftId,
        byte[] accessTokenHash,
        CancellationToken cancellationToken);
    Task<TenantProvisioningFulfillment?> GetForFulfillmentAsync(
        Guid draftId,
        CancellationToken cancellationToken);
    Task<TenantProvisioningPaymentVerification?> GetPaymentForVerificationAsync(
        Guid draftId,
        byte[] accessTokenHash,
        CancellationToken cancellationToken);
    Task MarkProvisionedAsync(
        Guid draftId,
        Guid tenantId,
        CancellationToken cancellationToken);
    Task MarkFailedAsync(
        Guid draftId,
        string error,
        CancellationToken cancellationToken);
}
