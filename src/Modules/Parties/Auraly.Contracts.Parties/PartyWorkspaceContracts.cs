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
}

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
    string RowVersion);

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

public sealed record SupplierAcceptance(Guid SupplierId, Guid PartyId, bool IdempotentReplay);
public sealed record CreateSellerRequest(Guid OperationId, Guid BusinessId, PartyInput Party, PartySiteInput PrimarySite, string Code, decimal? DefaultCommissionPercent, string CommissionBasis, string CommissionTrigger);
public sealed record CreateCarrierRequest(Guid OperationId, Guid BusinessId, PartyInput Party, PartySiteInput PrimarySite, string Code, string TransportationMode);
public sealed record CommercialRoleAcceptance(Guid RoleId, Guid PartyId, string Role, bool IdempotentReplay);
public sealed record CustomerPricingOption(Guid Id, string Code, string Name);
public sealed record CustomerPricingOptions(IReadOnlyCollection<CustomerPricingOption> PriceLists, IReadOnlyCollection<CustomerPricingOption> PriceChannels);
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
    bool IsPrimary);

public sealed record CustomerRoleDetail(
    Guid CustomerId,
    Guid? PriceListId,
    Guid? PriceChannelId,
    bool IsActive);

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

public sealed record PartyWorkspaceDetail(
    Guid PartyId,
    string PartyType,
    Guid IdentificationCountryId,
    string IdentificationTypeCode,
    string Identification,
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
    string RowVersion);

public sealed record PartyIdentityLookupResult(
    bool Exists,
    bool HasRequestedRole,
    PartyWorkspaceDetail? Party);