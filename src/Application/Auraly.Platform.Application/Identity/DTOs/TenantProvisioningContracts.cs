using Auraly.Contracts.TenantBilling;

namespace Auraly.Contracts.Tenants;

public sealed record ProvisionTenantRequest(
    Guid ProvisioningRequestId,
    string LegalName,
    string TradeName,
    string EntityType,
    string IdentificationTypeCode,
    string Nit,
    string? VerificationDigit,
    Guid CountryId,
    Guid AdministrativeDivisionId,
    Guid CityId,
    string Address,
    string Phone,
    string Email,
    string TaxResponsibilities,
    string BusinessName,
    string BusinessAddress,
    string BusinessPhone,
    string BusinessEmail,
    string TimeZone,
    string InventoryCostBasis,
    string InvitationEmail,
    int MaximumUsers,
    int MaximumEnrolledDevices);

public sealed record ProvisionTenantResult(
    Guid ProvisioningRequestId,
    Guid TenantId,
    Guid BusinessId,
    string TenantKey,
    Guid SalesWarehouseId,
    Guid OrdersWarehouseId,
    Guid DefaultCustomerId,
    Guid? AdministratorUserId,
    string Status);

public sealed record AcceptTenantInvitationRequest(
    string Token,
    string IdentificationType,
    string Identification,
    string FirstName,
    string LastName,
    string Username,
    string Phone,
    string Address,
    string Password,
    string PasswordConfirmation);


public sealed record TenantInvitationAdministratorProfile(
    string IdentificationType,
    string Identification,
    string FirstName,
    string LastName,
    string Username,
    string Phone,
    string Address);

public sealed record AcceptTenantInvitationResult(
    Guid TenantId,
    Guid UserId,
    string Username,
    string TenantKey,
    string Email,
    string Status);

public sealed record ResendTenantInvitationResult(
    Guid TenantId,
    string DeliveryEmail,
    DateTimeOffset ExpiresAt,
    string Status);

public sealed record TenantInvitationPasswordMaterial(
    string PasswordHash,
    byte[] OfflineSalt,
    byte[] OfflineHash,
    int OfflineIterations,
    DateTimeOffset ChangedAt);

public interface ITenantProvisioningStore
{
    Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid? actorUserId,
        TenantQuoteDto commercialQuote,
        CancellationToken cancellationToken);

    Task<AcceptTenantInvitationResult> AcceptInvitationAsync(
        byte[] tokenHash,
        TenantInvitationAdministratorProfile profile,
        TenantInvitationPasswordMaterial password,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ResendTenantInvitationResult?> ResendInvitationAsync(
        Guid tenantId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
