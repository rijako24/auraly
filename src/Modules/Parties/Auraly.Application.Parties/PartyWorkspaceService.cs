using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Auraly.Domain.Parties;

namespace Auraly.Application.Parties;

public interface IPartyWorkspaceStore
{
    Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct);
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
        if (role is not null && role is not ("Customer" or "Supplier"))
            throw new PartyValidationException("Role must be Customer or Supplier.");
        return store.PageAsync(actor, page, query with { Search = query.Search?.Trim(), Role = role }, ct);
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

