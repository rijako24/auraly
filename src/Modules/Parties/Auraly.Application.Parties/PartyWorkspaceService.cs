using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Auraly.Domain.Parties;

namespace Auraly.Application.Parties;

public interface IPartyWorkspaceStore
{
    Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct);
    Task<PartyWorkspaceDetail?> FindIdentityAsync(
        PartyActorIdentity actor, Guid countryId, string identificationType,
        string normalizedIdentification, CancellationToken ct);
    Task<PartyWorkspaceDetail?> GetDetailAsync(
        PartyActorIdentity actor, Guid partyId, CancellationToken ct);
    Task<PartyIdentityAcceptance> CreateIdentityAsync(
        PartyActorIdentity actor, Guid partyId, Guid siteId,
        CreatePartyIdentityRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct);
    Task<SupplierAcceptance> CreateSupplierAsync(
        PartyActorIdentity actor, Guid partyId, Guid supplierId, Guid siteId,
        CreateSupplierRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct);
    Task<PartyWorkspaceItem> UpdateAsync(
        PartyActorIdentity actor, Guid partyId, UpdatePartyRequest request,
        byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<PartyWorkspaceItem> SetStatusAsync(
        PartyActorIdentity actor, Guid partyId, SetPartyBusinessStatusRequest request,
        byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
}

public sealed class PartyWorkspaceService(
    IPartyWorkspaceStore store,
    IAuralyIdGenerator ids,
    TimeProvider time,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Read, PartyPermissionCodes.CustomerRead, PartyWorkspacePermissionCodes.SupplierRead);
        if (page < 1 || query.PageSize is < 1 or > 100)
            throw new PartyValidationException("Page and PageSize are outside the allowed range.");
        var role = query.Role?.Trim();
        if (role is not null && role is not ("Customer" or "Supplier" or "Seller" or "Carrier" or "Employee" or "User"))
            throw new PartyValidationException("Role must be Customer, Supplier, Seller, Carrier, Employee or User.");
        return store.PageAsync(actor, page, query with { Search = query.Search?.Trim(), Role = role }, ct);
    }

    public async Task<PartyIdentityLookupResult> FindIdentityAsync(
        PartyActorIdentity actor,
        Guid countryId,
        string identificationTypeCode,
        string identification,
        string requestedRole,
        CancellationToken ct)
    {
        Require(actor,
            PartyWorkspacePermissionCodes.Read,
            PartyPermissionCodes.CustomerCreate,
            PartyWorkspacePermissionCodes.SupplierCreate,
            PartyWorkspacePermissionCodes.SellerCreate,
            PartyWorkspacePermissionCodes.CarrierCreate,
            PartyWorkspacePermissionCodes.EmployeeCreate,
            PartyWorkspacePermissionCodes.UserCreate);
        if (countryId == Guid.Empty)
            throw new PartyValidationException("Identification country is required.");
        var role = requestedRole?.Trim();
        if (role is not ("Customer" or "Supplier" or "Seller" or "Carrier" or "Employee" or "User"))
            throw new PartyValidationException("Requested role is invalid.");
        if (string.IsNullOrWhiteSpace(identificationTypeCode))
            throw new PartyValidationException("Identification type is required.");
        var type = identificationTypeCode.Trim().ToUpperInvariant();
        var normalized = Translate(() =>
            PartyIdentityNormalizer.Normalize(type, identification));
        var party = await store.FindIdentityAsync(
            actor, countryId, type, normalized, ct);
        return new PartyIdentityLookupResult(
            party is not null,
            party?.Roles.Contains(role) == true,
            party);
    }

    public async Task<PartyWorkspaceDetail> GetDetailAsync(
        PartyActorIdentity actor, Guid partyId, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Read);
        if (partyId == Guid.Empty)
            throw new PartyValidationException("PartyId is required.");
        return await store.GetDetailAsync(actor, partyId, ct)
            ?? throw new PartyForbiddenException(
                "Party is outside the authenticated business.");
    }

    public Task<PartyIdentityAcceptance> CreateIdentityAsync(
        PartyActorIdentity actor, CreatePartyIdentityRequest request, CancellationToken ct)
    {
        var permission = request.TargetRole switch
        {
            "Employee" => PartyWorkspacePermissionCodes.EmployeeCreate,
            "User" => PartyWorkspacePermissionCodes.UserCreate,
            _ => throw new PartyValidationException("Target role must be Employee or User.")
        };
        Require(actor, permission);
        if (request.BusinessId != actor.BusinessId)
            throw new PartyForbiddenException("The Party business does not match the authenticated identity.");
        ValidateParty(request.Party);
        ValidateSite(request.PrimarySite);
        var normalized = Translate(() => PartyIdentityNormalizer.Normalize(
            request.Party.IdentificationTypeCode, request.Party.Identification));
        return store.CreateIdentityAsync(
            actor, ids.NewId(), ids.NewId(), request, normalized, time.GetUtcNow(), ct);
    }
    public Task<SupplierAcceptance> CreateSupplierAsync(
        PartyActorIdentity actor, CreateSupplierRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.SupplierCreate);
        if (request.BusinessId != actor.BusinessId)
            throw new PartyForbiddenException("The supplier business does not match the authenticated identity.");
        ValidateParty(request.Party);
        ValidateSite(request.PrimarySite);
        var normalized = Translate(() => PartyIdentityNormalizer.Normalize(
            request.Party.IdentificationTypeCode, request.Party.Identification));
        return store.CreateSupplierAsync(
            actor, ids.NewId(), ids.NewId(), ids.NewId(), request, normalized, time.GetUtcNow(), ct);
    }

    public async Task<PartyWorkspaceItem> UpdateAsync(
        PartyActorIdentity actor, Guid partyId, UpdatePartyRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Update);
        if (partyId == Guid.Empty) throw new PartyValidationException("PartyId is required.");
        if (request.PartyType is not PartyTypes.NaturalPerson and not PartyTypes.Organization)
            throw new PartyValidationException("Party type is invalid.");
        Translate(() => { PartyValidation.RequireText(request.DisplayName, "DisplayName", 200); return true; });
        if (request.PartyType == PartyTypes.Organization && string.IsNullOrWhiteSpace(request.LegalName))
            throw new PartyValidationException("Legal name is required for an organization.");
        var updated = await store.UpdateAsync(actor, partyId, request, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(actor.TenantId, actor.BusinessId, CancellationToken.None);
        return updated;
    }

    public async Task<PartyWorkspaceItem> SetStatusAsync(
        PartyActorIdentity actor, Guid partyId, SetPartyBusinessStatusRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Deactivate);
        if (partyId == Guid.Empty) throw new PartyValidationException("PartyId is required.");
        var updated = await store.SetStatusAsync(actor, partyId, request, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(actor.TenantId, actor.BusinessId, CancellationToken.None);
        return updated;
    }

    private static void ValidateParty(PartyInput party)
    {
        if (party.IdentificationCountryId == Guid.Empty)
            throw new PartyValidationException("Identification country is required.");
        if (party.PartyType is not PartyTypes.NaturalPerson and not PartyTypes.Organization)
            throw new PartyValidationException("Party type is invalid.");
        Translate(() => { PartyValidation.RequireText(party.DisplayName, "DisplayName", 200); return true; });
        if (party.PartyType == PartyTypes.Organization && string.IsNullOrWhiteSpace(party.LegalName))
            throw new PartyValidationException("Legal name is required for an organization.");
    }

    private static void ValidateSite(PartySiteInput site)
    {
        if (site.CountryId == Guid.Empty || site.AdministrativeDivisionId == Guid.Empty || site.CityId == Guid.Empty)
            throw new PartyValidationException("Country, administrative division and city are required.");
        Translate(() => PartyValidation.NormalizeCode(site.Code, "SiteCode", 32));
        Translate(() => { PartyValidation.RequireText(site.Name, "SiteName", 160); return true; });
        Translate(() => { PartyValidation.RequireText(site.AddressLine, "AddressLine", 300); return true; });
    }

    private static byte[] RowVersion(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new PartyValidationException("RowVersion is invalid.");
        }
    }

    private static T Translate<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException exception) { throw new PartyValidationException(exception.Message); }
    }

    private static void Require(PartyActorIdentity actor, params string[] permissions)
    {
        if (!permissions.Any(actor.Permissions.Contains))
            throw new PartyForbiddenException($"One of these permissions is required: {string.Join(", ", permissions)}.");
    }
}

