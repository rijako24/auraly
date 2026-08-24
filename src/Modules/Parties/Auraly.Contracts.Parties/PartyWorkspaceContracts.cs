namespace Auraly.Contracts.Parties;

public static class PartyWorkspacePermissionCodes
{
    public const string Read = "parties.read";
    public const string Update = "parties.update";
    public const string Deactivate = "parties.deactivate";
    public const string SupplierRead = "suppliers.read";
    public const string SupplierCreate = "suppliers.create";
    public const string SellerCreate = "sellers.create";
    public const string CarrierCreate = "carriers.create";
    public const string EmployeeCreate = "employees.create";
    public const string UserCreate = "users.create";
}

public sealed record CreatePartyIdentityRequest(
    Guid OperationId,
    Guid BusinessId,
    string TargetRole,
    PartyInput Party,
    PartySiteInput PrimarySite);

public sealed record PartyIdentityAcceptance(Guid PartyId, bool ExistingIdentity);

public sealed record CreateSupplierRequest(
    Guid OperationId,
    Guid BusinessId,
    PartyInput Party,
    PartySiteInput PrimarySite);

public sealed record UpdatePartyRequest(
    string PartyType,
    string DisplayName,
    string? LegalName,
    string? FirstName,
    string? LastName,
    string? VerificationDigit,
    string? Email,
    string? Phone,
    string RowVersion,
    IReadOnlyCollection<PartySiteSaveInput>? Sites = null,
    UpdateCustomerRoleRequest? Customer = null,
    UpdateSellerRoleRequest? Seller = null,
    UpdateCarrierRoleRequest? Carrier = null);

public sealed record UpdateCustomerRoleRequest(
    Guid? PriceChannelId,
    bool RequiresElectronicInvoice,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null);

public sealed record UpdateSellerRoleRequest(
    string Code,
    decimal? DefaultCommissionPercent,
    string CommissionBasis,
    string CommissionTrigger);

public sealed record UpdateCarrierRoleRequest(string Code, string TransportationMode);

public sealed record PartySiteSaveInput(
    Guid? PartySiteId,
    string? RowVersion,
    PartySiteInput Site);

public sealed record SetPartyBusinessStatusRequest(bool IsActive, string RowVersion);

public sealed record PartyWorkspaceQuery(
    int PageSize = 25,
    string? Search = null,
    string? Role = null,
    bool? IsActive = null,
    bool? IsIncomplete = null);

public sealed record PartyWorkspaceItem(
    Guid PartyId,
    string PartyType,
    string? IdentificationTypeCode,
    string? Identification,
    string? VerificationDigit,
    string DisplayName,
    string? LegalName,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    IReadOnlyCollection<string> Roles,
    string? PrimarySiteName,
    string? CityName,
    bool IsActive,
    string CompletionStatus,
    string RowVersion);

public sealed record PartyWorkspacePage(
    IReadOnlyCollection<PartyWorkspaceItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CustomerMapQuery(string? Search = null, Guid? RouteId = null, Guid? SellerId = null, bool OnlyUnassigned = false);
public sealed record CustomerMapAssignment(Guid RouteId, string RouteName, Guid SellerId, string SellerName);
public sealed record CustomerMapSite(
    Guid CustomerId, Guid PartyId, string CustomerName, string? Identification,
    Guid PartySiteId, string SiteName, string AddressLine, string? Neighborhood,
    string CityName, string? Phone, string? GoogleMapsUrl, decimal? Latitude, decimal? Longitude,
    IReadOnlyCollection<CustomerMapAssignment> Assignments);

public sealed record SupplierAcceptance(Guid SupplierId, Guid PartyId, bool IdempotentReplay);
public sealed record CreateSellerRequest(Guid OperationId, Guid BusinessId, PartyInput Party, PartySiteInput PrimarySite, string Code, decimal? DefaultCommissionPercent, string CommissionBasis, string CommissionTrigger);
public sealed record CreateCarrierRequest(Guid OperationId, Guid BusinessId, PartyInput Party, PartySiteInput PrimarySite, string Code, string TransportationMode);
public sealed record CommercialRoleAcceptance(Guid RoleId, Guid PartyId, string Role, bool IdempotentReplay);
public sealed record CustomerPricingOption(Guid Id, string Code, string Name);
public sealed record CustomerPricingOptions(IReadOnlyCollection<CustomerPricingOption> PriceChannels);
public sealed record PartyWorkspaceSiteDetail(
    Guid PartySiteId,
    string Code,
    string Name,
    Guid CountryId,
    Guid AdministrativeDivisionId,
    Guid CityId,
    string AddressLine,
    string? Neighborhood,
    string? PostalCode,
    string? Email,
    string? Phone,
    bool IsPrimary,
    bool IsActive = true,
    string? GoogleMapsUrl = null,
    string? GooglePlaceId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string RowVersion = "");

public sealed record CustomerRoleDetail(
    Guid CustomerId,
    Guid? PriceChannelId,
    bool IsActive,
    bool RequiresElectronicInvoice,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil);

public sealed record SupplierRoleDetail(Guid SupplierId, bool IsActive);

public sealed record SellerRoleDetail(
    Guid SellerId,
    string Code,
    decimal? DefaultCommissionPercent,
    string CommissionBasis,
    string CommissionTrigger,
    bool IsActive);

public sealed record CarrierRoleDetail(
    Guid CarrierId,
    string Code,
    string TransportationMode,
    bool IsActive);

public sealed record EmployeeRoleDetail(Guid EmployeeId, bool IsActive);

public sealed record PartyUserRoleAssignment(
    Guid RoleId,
    string RoleName,
    Guid? BusinessId,
    DateTimeOffset AssignedAt);

public sealed record UserRoleDetail(
    Guid UserId,
    string Username,
    string Email,
    bool IsActive,
    IReadOnlyList<PartyUserRoleAssignment> Roles);

public sealed record PartyWorkspaceDetail(
    Guid PartyId,
    string PartyType,
    Guid? IdentificationCountryId,
    string? IdentificationTypeCode,
    string? Identification,
    string? VerificationDigit,
    string DisplayName,
    string? LegalName,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    IReadOnlyCollection<string> Roles,
    PartyWorkspaceSiteDetail? PrimarySite,
    CustomerRoleDetail? Customer,
    SupplierRoleDetail? Supplier,
    SellerRoleDetail? Seller,
    CarrierRoleDetail? Carrier,
    EmployeeRoleDetail? Employee,
    UserRoleDetail? User,
    string RowVersion,
    IReadOnlyCollection<PartyWorkspaceSiteDetail>? Sites = null);

public sealed record PartyIdentityLookupResult(
    bool Exists,
    bool HasRequestedRole,
    PartyWorkspaceDetail? Party);
