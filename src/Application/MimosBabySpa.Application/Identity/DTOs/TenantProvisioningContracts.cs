namespace Auraly.Contracts.Tenants;

public sealed record ProvisionTenantRequest(
    Guid ProvisioningRequestId,
    string LegalName,
    string TradeName,
    string Nit,
    string VerificationDigit,
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
    string AdministratorIdentificationType,
    string AdministratorIdentification,
    string AdministratorFirstName,
    string AdministratorLastName,
    string AdministratorEmail,
    string AdministratorPhone);

public sealed record ProvisionTenantResult(
    Guid ProvisioningRequestId,
    Guid TenantId,
    Guid BusinessId,
    Guid SalesWarehouseId,
    Guid OrdersWarehouseId,
    Guid DefaultCustomerId,
    Guid AdministratorUserId,
    string Status);

public sealed record AcceptTenantInvitationRequest(
    string Token,
    string Password,
    string PasswordConfirmation);

public sealed record AcceptTenantInvitationResult(
    Guid TenantId,
    Guid UserId,
    string Email,
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
        CancellationToken cancellationToken);

    Task<AcceptTenantInvitationResult> AcceptInvitationAsync(
        byte[] tokenHash,
        TenantInvitationPasswordMaterial password,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
