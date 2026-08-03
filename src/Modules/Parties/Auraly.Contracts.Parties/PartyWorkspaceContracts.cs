namespace Auraly.Contracts.Parties;

public static class PartyWorkspacePermissionCodes
{
    public const string Read = "parties.read";
    public const string Update = "parties.update";
    public const string Deactivate = "parties.deactivate";
    public const string SupplierRead = "suppliers.read";
    public const string SupplierCreate = "suppliers.create";
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
